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
using MonoMod.Cil;
using System;
using System.Collections.ObjectModel;

namespace ThinkInvisible.Dronemeld {
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [BepInDependency(R2API.R2API.PluginGUID, R2API.R2API.PluginVersion)]
    [BepInDependency(TILER2Plugin.ModGuid, TILER2Plugin.ModVer)]
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(PrefabAPI), nameof(RecalculateStatsAPI))]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public class DronemeldPlugin:BaseUnityPlugin {
        public const string ModVer = "1.1.0";
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

            [AutoConfig("If true, turrets and other immobile drones will remember all their purchase locations and constantly teleport to the nearest one to their owner.")]
            [AutoConfigRoOCheckbox()]
            public bool quantumTurrets { get; internal set; } = true;

            [AutoConfig("Which CharacterMaster prefab names to apply Dronemeld to when spawned via an interactable purchase (SummonMasterBehavior). Comma-delimited, whitespace is trimmed.")]
            [AutoConfigRoOString()]
            public string masterWhitelist { get; internal set; } = "Drone1Master, Drone2Master, DroneMissileMaster, FlameDroneMaster, MegaDroneMaster, Turret1Master, DroneBackupMaster, BeetleGuardAllyMaster, VoidMegaCrabAllyMaster, VoidJailerAllyMaster, NullifierAllyMaster, MinorConstructOnKillMaster, SquidTurretMaster";

            [AutoConfig("If true, Dronemeld will apply to Goobos Jr.")]
            [AutoConfigRoOCheckbox()]
            public bool applyToGoobo { get; internal set; } = true;

            [AutoConfig("Which CharacterMaster prefab names to apply QuantumTurrets behavior to. Comma-delimited, whitespace is trimmed.")]
            [AutoConfigRoOString()]
            public string quantumWhitelist { get; internal set; } = "Turret1Master, MinorConstructOnKillMaster, SquidTurretMaster";
        }

        public class ClientConfig : AutoConfigContainer {
            [AutoConfig("If greater than 0, applies a visual size increase of this percentage to drones per Dronemeld stack.", AutoConfigFlags.DeferUntilNextStage)]
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

            On.RoR2.Run.Start += Run_Start;
            On.RoR2.Skills.SkillDef.OnFixedUpdate += SkillDef_OnFixedUpdate;
            RecalculateStatsAPI.GetStatCoefficients += RecalculateStatsAPI_GetStatCoefficients;
            On.RoR2.CharacterBody.GetDisplayName += CharacterBody_GetDisplayName;
            On.RoR2.CharacterMaster.OnBodyStart += CharacterMaster_OnBodyStart;
            On.EntityStates.Drone.DeathState.OnImpactServer += DeathState_OnImpactServer;
            On.RoR2.MasterSummon.Perform += MasterSummon_Perform;
            On.RoR2.Projectile.GummyCloneProjectile.SpawnGummyClone += GummyCloneProjectile_SpawnGummyClone;
            On.RoR2.DirectorCore.TrySpawnObject += DirectorCore_TrySpawnObject;
            IL.RoR2.CharacterMaster.GetDeployableCount += CharacterMaster_GetDeployableCount;
            IL.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
        }

        void UpdateMasterWhitelist() {
            _masterWhitelist.Clear();
            _masterWhitelist.UnionWith(serverConfig.masterWhitelist.Split(',').Select(x => x.Trim()));
        }

        CharacterMaster TryApply(IEnumerable<CharacterMaster> targetMasters) {
            if(targetMasters.Count() >= serverConfig.maxDronesPerType) {
                var dm = serverConfig.priorityOrder switch {
                    DronemeldPriorityOrder.Random => rng.NextElementUniform(targetMasters.ToArray()),
                    DronemeldPriorityOrder.FirstOnly => targetMasters.First(),
                    DronemeldPriorityOrder.RoundRobin => targetMasters.OrderBy(d => d.inventory.GetItemCount(stackItem)).First(),
                    _ => throw new System.InvalidOperationException("Encountered invalid value of serverConfig.priorityOrder.")
                };

                if(dm.TryGetComponent<MasterSuicideOnTimer>(out var mst)) {
                    dm.gameObject.AddComponent<TimedDronemeldStack>().Activate(mst.lifeTimer - mst.timer);
                    mst.timer = 0f;
                } else {
                    dm.inventory.GiveItem(stackItem);
                    var db = dm.GetBody();
                    if(db) new MsgAddDroneSize(db.gameObject).Send(R2API.Networking.NetworkDestination.Clients);
                }

                return dm;
            }
            return null;
        }

        #region Hooks
        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self) {
            orig(self);
            if(!NetworkServer.active) return;
            rng = new Xoroshiro128Plus(self.seed);
        }

        private CharacterMaster MasterSummon_Perform(On.RoR2.MasterSummon.orig_Perform orig, MasterSummon self) {
            if(self.masterPrefab
                && _masterWhitelist.Contains(self.masterPrefab.name)
                && self.summonerBodyObject
                && self.summonerBodyObject.TryGetComponent<CharacterBody>(out var actiBody)
                && actiBody.master) {
                var extantDronesOfType = CharacterMaster.readOnlyInstancesList.Where(m =>
                    (serverConfig.perPlayer ? (m.minionOwnership.ownerMaster == actiBody.master) : (m.teamIndex == actiBody.master.teamIndex))
                    && m.gameObject.name.Replace("(Clone)", "") == self.masterPrefab.name);
                var result = TryApply(extantDronesOfType);
                if(result) {
                    if(serverConfig.quantumWhitelist.Contains(result.gameObject.name.Replace("(Clone)", ""))) {
                        var qt = result.gameObject.AddComponent<DronemeldQuantumTurret>();
                        if(!qt)
                            result.gameObject.AddComponent<DronemeldQuantumTurret>();
                        else
                            qt.RegisterLocation(self.position);
                    }

                    return null;
                }
            }
            return orig(self);
        }

        private GameObject DirectorCore_TrySpawnObject(On.RoR2.DirectorCore.orig_TrySpawnObject orig, DirectorCore self, DirectorSpawnRequest directorSpawnRequest) {
            if(_masterWhitelist.Contains(directorSpawnRequest.spawnCard.prefab.name)
                && directorSpawnRequest.summonerBodyObject
                && directorSpawnRequest.summonerBodyObject.TryGetComponent<CharacterBody>(out var summonerBody)
                && summonerBody.master) {
                var extantDronesOfType = CharacterMaster.readOnlyInstancesList.Where(m =>
                    (serverConfig.perPlayer ? (m.minionOwnership.ownerMaster == summonerBody.master) : (m.teamIndex == summonerBody.master.teamIndex))
                    && m.gameObject.name.Replace("(Clone)", "") == directorSpawnRequest.spawnCard.prefab.name);
                var result = TryApply(extantDronesOfType);
                if(result) {
                    if(serverConfig.quantumWhitelist.Contains(result.gameObject.name.Replace("(Clone)", ""))) {
                        var qt = result.gameObject.AddComponent<DronemeldQuantumTurret>();
                        if(!qt)
                            result.gameObject.AddComponent<DronemeldQuantumTurret>();
                        else {
                            var retv = orig(self, directorSpawnRequest); //retrieve spawn position by actually spawning the object, then destroying it
                            if(retv) {
                                qt.RegisterLocation(retv.transform.position);
                                retv.GetComponent<CharacterMaster>().TrueKill();
                                result.inventory.RemoveItem(stackItem); //remove duplicate stack caused by this
                            }
                        }
                    }

                    return null;
                }
            }
            return orig(self, directorSpawnRequest);
        }

        private void CharacterMaster_GetDeployableCount(ILContext il) {
            ILCursor c = new(il);

            if(c.TryGotoNext(MoveType.Before,
                i => i.MatchLdfld<DeployableInfo>(nameof(DeployableInfo.slot))
                )) {
                DeployableInfo capturedDI = default;
                c.EmitDelegate<Func<DeployableInfo, DeployableInfo>>(di => { capturedDI = di; return di; });
                if(c.TryGotoNext(MoveType.After,
                    i => i.MatchLdloc(0),
                    i => i.MatchLdcI4(1),
                    i => i.MatchAdd(),
                    i => i.MatchStloc(0))) {
                    c.Index--;
                    c.EmitDelegate<Func<int, int>>(addend => {
                        if(capturedDI.deployable && capturedDI.deployable.TryGetComponent<Inventory>(out var inv)) {
                            return inv.GetItemCount(stackItem) + addend;
                        }
                        return addend;
                    });
                } else {
                    _logger.LogError("Failed to apply IL patch: CharacterMaster_GetDeployableCount, part 2. Dronemeld will fail to work or have unexpected results on certain spawn methods.");
                }
            } else {
                _logger.LogError("Failed to apply IL patch: CharacterMaster_GetDeployableCount, part 1. Dronemeld will fail to work or have unexpected results on certain spawn methods.");
            }
        }

        private void CharacterBody_RecalculateStats(ILContext il) {
            ILCursor c = new(il);
            if(c.TryGotoNext(MoveType.After,
                i => i.MatchCallOrCallvirt<TeamComponent>(nameof(TeamComponent.GetTeamMembers)),
                i => i.MatchCallOrCallvirt(out _)
                )) {
                c.Index--;
                c.Remove();
                c.EmitDelegate<Func<ReadOnlyCollection<TeamComponent>, int>>(members => members.Sum(m => 
                    (m.TryGetComponent<CharacterBody>(out var cb) && cb.inventory)
                    ? (1 + cb.inventory.GetItemCount(stackItem))
                    : 1));
            } else {
                _logger.LogError("Failed to apply IL patch: CharacterBody_RecalculateStats. Empathy Cores will not count Dronemeld stacks for their per-ally stat boost.");
            }
        }

        private void GummyCloneProjectile_SpawnGummyClone(On.RoR2.Projectile.GummyCloneProjectile.orig_SpawnGummyClone orig, RoR2.Projectile.GummyCloneProjectile self) {
            if(serverConfig.applyToGoobo && self.TryGetComponent<RoR2.Projectile.ProjectileController>(out var pc) && pc.owner && pc.owner.TryGetComponent<CharacterBody>(out var ob) && ob.master) {
                var extantGoobos = CharacterMaster.readOnlyInstancesList.Where(m =>
                    (serverConfig.perPlayer ? (m.minionOwnership.ownerMaster == ob.master) : (m.teamIndex == ob.master.teamIndex))
                    && m.inventory.GetItemCount(DLC1Content.Items.GummyCloneIdentifier) > 0);
                if(TryApply(extantGoobos))
                    return;
            }
            orig(self);
        }

        private void CharacterMaster_OnBodyStart(On.RoR2.CharacterMaster.orig_OnBodyStart orig, CharacterMaster self, CharacterBody body) {
            orig(self, body);
            var stacks = self.inventory.GetItemCount(stackItem);
            if(NetworkClient.active && body.modelLocator && clientConfig.vfxResize > 0f && stacks > 0) {
                body.modelLocator.modelTransform.localScale += Vector3.one * clientConfig.vfxResize * self.inventory.GetItemCount(stackItem);
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
                    return $"{retv} <style=cStack>x{stacks + 1}</style>";
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
                body.modelLocator.modelTransform.localScale += Vector3.one * clientConfig.vfxResize;
            }
        }
        public struct MsgRemoveDroneSize : INetMessage {
            GameObject _target;

            public MsgRemoveDroneSize(GameObject target) {
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
                body.modelLocator.modelTransform.localScale -= Vector3.one * clientConfig.vfxResize;
            }
        }

        [RequireComponent(typeof(CharacterMaster))]
        public class DronemeldQuantumTurret : MonoBehaviour {
            readonly List<Vector3> storedStates = new();
            CharacterMaster master;

            const float EPSILON = 5f;

            public void RegisterLocation(Vector3 pos) {
                var mObj = master.GetBodyObject();
                if(!mObj || Vector3.Distance(mObj.transform.position, pos) < EPSILON) return;
                storedStates.Add(pos);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine")]
            void Awake() {
                master = GetComponent<CharacterMaster>();
                Stage.onServerStageComplete += Stage_onServerStageComplete;
            }

            private void Stage_onServerStageComplete(Stage obj) {
                storedStates.Clear();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine")]
            void FixedUpdate() {
                if(!NetworkServer.active || storedStates.Count < 1) return;
                var owner = master.minionOwnership.ownerMaster;
                if(!owner) return;
                var ownerObj = owner.GetBodyObject();
                if(!ownerObj) return;
                var mObj = master.GetBodyObject();
                if(!mObj) return;

                var ownerDist = Vector3.Distance(ownerObj.transform.position, mObj.transform.position);
                var closest = storedStates.OrderBy(state => Vector3.Distance(ownerObj.transform.position, state)).First();
                var closestDist = Vector3.Distance(ownerObj.transform.position, closest);
                if(closestDist < ownerDist) {
                    storedStates.Remove(closest);
                    storedStates.Add(mObj.transform.position);
                    TeleportHelper.TeleportGameObject(mObj, closest);
                }
            }
        }

        [RequireComponent(typeof(CharacterMaster))]
        public class TimedDronemeldStack : MonoBehaviour {
            float stopwatch = 0f;
            bool started = false;
            CharacterMaster boundCM;

            public void Activate(float time) {
                if(started) return;
                boundCM.inventory.GiveItem(stackItem);
                var cb = boundCM.GetBody();
                if(cb) new MsgAddDroneSize(cb.gameObject).Send(R2API.Networking.NetworkDestination.Clients);
                stopwatch = time;
                started = true;
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine")]
            void Awake() {
                boundCM = GetComponent<CharacterMaster>();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by Unity Engine")]
            void FixedUpdate() {
                if(!started) return;
                stopwatch -= Time.fixedDeltaTime;
                if(stopwatch <= 0f) {
                    boundCM.inventory.RemoveItem(stackItem);
                    var cb = boundCM.GetBody();
                    if(cb) new MsgRemoveDroneSize(cb.gameObject).Send(R2API.Networking.NetworkDestination.Clients);
                    Destroy(this);
                }
            }
        }
    }
}
