using BepInEx;
using R2API;
using R2API.Utils;
using UnityEngine;
using BepInEx.Configuration;
using Path = System.IO.Path;
using TILER2;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine.Networking;
using R2API.Networking.Interfaces;

namespace ThinkInvisible.Dronemeld {
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [BepInDependency(R2API.R2API.PluginGUID, R2API.R2API.PluginVersion)]
    [BepInDependency(TILER2Plugin.ModGuid, TILER2Plugin.ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(PrefabAPI), nameof(RecalculateStatsAPI))]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public class DronemeldPlugin:BaseUnityPlugin {
        public const string ModVer = "1.0.0";
        public const string ModName = "Dronemeld";
        public const string ModGuid = "com.ThinkInvisible.Dronemeld";

        private static ConfigFile cfgFile;
        
        internal static BepInEx.Logging.ManualLogSource _logger;

        public enum DronemeldPriorityOrder {
            RoundRobin, Random, FirstOnly
        }

        public class ServerConfig : AutoConfigContainer {
            [AutoConfig("If true, counts duplicate drones per player. If false, counts duplicate drones globally (meaning players who buy a drone may not benefit from it directly).", AutoConfigFlags.None)]
            [AutoConfigRoOCheckbox()]
            public bool perPlayer { get; internal set; } = true;

            [AutoConfig("The maximum number of drones of a given type to allow.", AutoConfigFlags.None, 1, int.MaxValue)]
            [AutoConfigRoOIntSlider("{0:N0}", 1, 10)]
            public int maxDronesPerType { get; internal set; } = 1;

            [AutoConfig("Which order to perform upgrades in when MaxDronesPerType > 1.")]
            [AutoConfigRoOChoice()]
            public DronemeldPriorityOrder priorityOrder { get; internal set; } = DronemeldPriorityOrder.RoundRobin;

            [AutoConfig("Added to multiplier on drone base health per stack of Dronemeld.")]
            [AutoConfigRoOSlider("+{0:N2}x", 0f, 2f)]
            public float statMultHealth { get; internal set; } = 1f;

            [AutoConfig("Added to multiplier on drone base damage per stack of Dronemeld.")]
            [AutoConfigRoOSlider("+{0:N2}x", 0f, 2f)]
            public float statMultDamage { get; internal set; } = 0.6f;

            [AutoConfig("Added to multiplier on drone base attack speed per stack of Dronemeld.")]
            [AutoConfigRoOSlider("+{0:N2}x", 0f, 2f)]
            public float statMultAttackSpeed { get; internal set; } = 0.6f;

            [AutoConfig("Added to multiplier on drone cooldown speed per stack of Dronemeld (1x = skills recharge twice as fast).")]
            [AutoConfigRoOSlider("+{0:N2}x", 0f, 2f)]
            public float statMultCDR { get; internal set; } = 0.6f;

            [AutoConfig("Which CharacterMaster prefab names to apply Dronemeld to when spawned via an interactable purchase (SummonMasterBehavior). Comma-delimited, whitespace is trimmed.")]
            [AutoConfigRoOString()]
            public string masterWhitelist { get; internal set; } = "Drone1Master, Drone2Master, DroneMissileMaster, FlameDroneMaster, MegaDroneMaster, Turret1Master";
        }

        public class ClientConfig : AutoConfigContainer {
            [AutoConfig("If greater than 0, applies a visual size increase of this percentage to drones per Dronemeld stack.", AutoConfigFlags.None)]
            [AutoConfigRoOSlider("{0:P0}", 0f, 1f)]
            public float vfxResize { get; internal set; } = 0.25f;
        }

        public static ServerConfig serverConfig = new();
        public static ClientConfig clientConfig = new();

        private static readonly HashSet<string> _masterWhitelist = new();
        private Xoroshiro128Plus rng;
        public static ItemDef stackItem;

        private void Awake() {
            _logger = Logger;

            cfgFile = new ConfigFile(Path.Combine(Paths.ConfigPath, ModGuid + ".cfg"), true);

            serverConfig.ConfigEntryChanged += (sender, args) => {
                if(args.target.boundProperty.Name == nameof(serverConfig.masterWhitelist)) {
                    UpdateMasterWhitelist();
                }
            };

            serverConfig.BindAll(cfgFile, "Dronemeld", "Server");
            clientConfig.BindAll(cfgFile, "Dronemeld", "Client");

            UpdateMasterWhitelist();

            stackItem = ScriptableObject.CreateInstance<ItemDef>();
            stackItem.deprecatedTier = ItemTier.NoTier;
            stackItem.canRemove = false;
            stackItem.hidden = true;
            stackItem.nameToken = "ITEM_DRONEMELD_STACK_NAME";
            stackItem.loreToken = "";
            stackItem.descriptionToken = "";
            stackItem.pickupToken = "";
            stackItem.name = "DronemeldInternalStackItem";
            stackItem.tags = new ItemTag[] { };
            ContentAddition.AddItemDef(stackItem);

            R2API.Networking.NetworkingAPI.RegisterMessageType<MsgAddDroneSize>();

            On.RoR2.SummonMasterBehavior.OpenSummonReturnMaster += SummonMasterBehavior_OpenSummonReturnMaster;
            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Skills.SkillDef.OnFixedUpdate += SkillDef_OnFixedUpdate;
            RecalculateStatsAPI.GetStatCoefficients += RecalculateStatsAPI_GetStatCoefficients;
            On.RoR2.CharacterBody.GetDisplayName += CharacterBody_GetDisplayName;
            On.RoR2.CharacterMaster.OnBodyStart += CharacterMaster_OnBodyStart;
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
        }

        void UpdateMasterWhitelist() {
            _masterWhitelist.Clear();
            _masterWhitelist.UnionWith(serverConfig.masterWhitelist.Split(',').Select(x => x.Trim()));
        }

        #region Hooks
        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self) {
            orig(self);
            if(!NetworkServer.active) return;
            rng = new Xoroshiro128Plus(self.seed);
        }

        private CharacterMaster SummonMasterBehavior_OpenSummonReturnMaster(On.RoR2.SummonMasterBehavior.orig_OpenSummonReturnMaster orig, SummonMasterBehavior self, Interactor activator) {
            if(self.masterPrefab && _masterWhitelist.Contains(self.masterPrefab.name) && activator) {
                var actiMaster = activator.gameObject.GetComponent<CharacterBody>().master;
                if(!actiMaster) return orig(self, activator);
                var extantDronesOfType = CharacterMaster.readOnlyInstancesList.Where(m =>
                    (serverConfig.perPlayer ? (m.minionOwnership.ownerMaster == actiMaster) : (m.teamIndex == actiMaster.teamIndex))
                    && m.gameObject.name.Replace("(Clone)", "") == self.masterPrefab.name);
                if(extantDronesOfType.Count() >= serverConfig.maxDronesPerType) {
                    var dm = serverConfig.priorityOrder switch {
                        DronemeldPriorityOrder.Random => rng.NextElementUniform(extantDronesOfType.ToArray()),
                        DronemeldPriorityOrder.FirstOnly => extantDronesOfType.First(),
                        DronemeldPriorityOrder.RoundRobin => extantDronesOfType.OrderBy(d => d.inventory.GetItemCount(stackItem)).First(),
                        _ => throw new System.InvalidOperationException("Encountered invalid value of serverConfig.priorityOrder.")
                    };

                    dm.inventory.GiveItem(stackItem);
                    var db = dm.GetBody();
                    if(db) new MsgAddDroneSize(db.gameObject).Send(R2API.Networking.NetworkDestination.Clients);

                    return dm;
                }
            }
            return orig(self, activator);
        }

        private void CharacterMaster_OnBodyStart(On.RoR2.CharacterMaster.orig_OnBodyStart orig, CharacterMaster self, CharacterBody body) {
            orig(self, body);
            var stacks = self.inventory.GetItemCount(stackItem);
            if(NetworkClient.active && body.modelLocator && clientConfig.vfxResize > 0f && stacks > 0) {
                body.modelLocator.transform.localScale += Vector3.one * clientConfig.vfxResize * self.inventory.GetItemCount(stackItem);
            }
        }

        private void RecalculateStatsAPI_GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args) {
            if(sender && sender.master) {
                var stacks = sender.master.inventory.GetItemCount(stackItem);
                if(stacks <= 0) return;
                args.baseHealthAdd += (sender.baseMaxHealth + sender.levelMaxHealth * sender.level) * stacks * serverConfig.statMultHealth;
                args.baseDamageAdd += (sender.baseDamage + sender.levelDamage * sender.level) * stacks * serverConfig.statMultDamage;
                args.baseAttackSpeedAdd += (sender.baseAttackSpeed + sender.levelAttackSpeed * sender.level) * stacks * serverConfig.statMultAttackSpeed;
            }
        }

        private void SkillDef_OnFixedUpdate(On.RoR2.Skills.SkillDef.orig_OnFixedUpdate orig, RoR2.Skills.SkillDef self, GenericSkill skillSlot) {
            if(skillSlot && skillSlot.characterBody && skillSlot.characterBody.master) {
                var stacks = skillSlot.characterBody.master.inventory.GetItemCount(stackItem);
                if(stacks > 0)
                    skillSlot.RunRecharge(Time.fixedDeltaTime * stacks * serverConfig.statMultCDR);
            }
            orig(self, skillSlot);
        }

        private void DeathState_OnImpactServer(On.EntityStates.Drone.DeathState.orig_OnImpactServer orig, EntityStates.Drone.DeathState self, Vector3 contactPoint) {
            orig(self, contactPoint);
            var count = self.characterBody.master.inventory.GetItemCount(stackItem);
            for(var i = 0; i < count; i++)
                orig(self, contactPoint);
        }

        private string CharacterBody_GetDisplayName(On.RoR2.CharacterBody.orig_GetDisplayName orig, CharacterBody self) {
            var retv = orig(self);
            if(self && self.master) {
                var stacks = self.master.inventory.GetItemCount(stackItem);
                if(stacks > 0) {
                    return $"{retv} <color=#AAAAAA>x{stacks + 1}</color>";
                }
            }
            return retv;
        }
        #endregion

        public struct MsgAddDroneSize : INetMessage {
            GameObject _target;

            public MsgAddDroneSize(GameObject target) {
                _target = target;
            }

            public void Serialize(NetworkWriter writer) {
                writer.Write(_target);
            }

            public void Deserialize(NetworkReader reader) {
                _target = reader.ReadGameObject();
            }

            public void OnReceived() {
                if(!_target || clientConfig.vfxResize == 0) return;
                var body = _target.GetComponent<CharacterBody>();
                if(!body || !body.modelLocator) return;
                body.modelLocator.transform.localScale += Vector3.one * clientConfig.vfxResize;
            }
        }
    }
}
