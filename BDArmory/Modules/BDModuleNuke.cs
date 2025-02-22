using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Module;
using BDArmory.FX;
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace BDArmory.Modules
{
    class BDModuleNuke : PartModule
    {
        [KSPField(isPersistant = true, guiActive = true, guiName = "WARNING: Reactor Safeties:", guiActiveEditor = false), UI_Label(affectSymCounterparts = UI_Scene.All, scene = UI_Scene.All)]//Weapon Name
        public string status = "OFFLINE";

        [KSPField(isPersistant = true, guiActive = true, guiName = "Coolant Remaining", guiActiveEditor = false), UI_Label(scene = UI_Scene.All)]
        public double fuelleft;

        [KSPField]
        public static string flashModelPath = "BDArmory/Models/explosion/nuke/nukeFlash";
        [KSPField]
        public static string shockModelPath = "BDArmory/Models/explosion/nuke/nukeShock";
        [KSPField]
        public static string blastModelPath = "BDArmory/Models/explosion/nuke/nukeBlast";
        [KSPField]
        public static string plumeModelPath = "BDArmory/Models/explosion/nuke/nukePlume";
        [KSPField]
        public static string debrisModelPath = "BDArmory/Models/explosion/nuke/nukeScatter";
        [KSPField]
        public static string blastSoundPath = "BDArmory/Models/explosion/nuke/nukeBoom";

        [KSPField(isPersistant = true)]
        public float thermalRadius = 750;

        [KSPField(isPersistant = true)]
        public float yield = 0.05f;

        [KSPField(isPersistant = true)]
        public float fluence = 0.05f;

        [KSPField(isPersistant = true)]
        public bool isEMP = false;

        [KSPField(isPersistant = true)]
        public bool engineCore = false;

        [KSPField(isPersistant = true)]
        public float meltDownDuration = 2.5f;

        private int FuelID;
        private bool hasDetonated = false;
        private bool goingCritical = false;
        public string Sourcevessel;

        [KSPField(isPersistant = true)]
        public string reportingName = "Reactor Containment Failure";

        MissileLauncher missile;
        public MissileLauncher Launcher
        {
            get
            {
                if (missile) return missile;
                missile = missile = part.FindModuleImplementing<MissileLauncher>();
                return missile;
            }
        }

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (engineCore)
                {
                    FuelID = PartResourceLibrary.Instance.GetDefinition("LiquidFuel").id;
                    vessel.GetConnectedResourceTotals(FuelID, out double fuelCurrent, out double fuelMax);
                    fuelleft = fuelCurrent;
                    var engine = part.FindModuleImplementing<ModuleEngines>();
                    if (engine != null)
                    {
                        engine.allowShutdown = false;
                    }
                    part.force_activate();
                }
                else
                {
                    Fields["status"].guiActive = false;
                    Fields["fuelleft"].guiActive = false;
                    var missile = part.FindModuleImplementing<MissileLauncher>();
                }
                Sourcevessel = part.vessel.GetName();

                if (engineCore) part.OnJustAboutToBeDestroyed += Detonate;
                GameEvents.onVesselPartCountChanged.Add(CheckAttached);
                GameEvents.onVesselCreate.Add(CheckAttached);
            }
            base.OnStart(state);
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDACompetitionMode.Instance.competitionIsActive) //only begin checking engine state after comp start
                {
                    if (engineCore)
                    {
                        vessel.GetConnectedResourceTotals(FuelID, out double fuelCurrent, out double fuelMax);
                        fuelleft = fuelCurrent;
                        if (fuelleft <= 0)
                        {
                            if (!hasDetonated && !goingCritical)
                            {
                                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is out of fuel.");
                                StartCoroutine(DelayedDetonation(meltDownDuration)); //bingo fuel, detonate
                            }
                        }
                        var engine = part.FindModuleImplementing<ModuleEngines>();
                        if (engine != null)
                        {
                            if (!engine.isEnabled || !engine.EngineIgnited)
                            {
                                if (!hasDetonated)
                                {
                                    if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is Off, detonating");
                                    Detonate(); //nuke engine off after comp start, detonate.
                                }
                            }
                            if (engine.thrustPercentage < 100)
                            {
                                if (part.Modules.GetModule<HitpointTracker>().Hitpoints == part.Modules.GetModule<HitpointTracker>().GetMaxHitpoints())
                                {
                                    if (!hasDetonated)
                                    {
                                        if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.RWPS3R2NukeModule]: nerva on " + Sourcevessel + " is manually thrust limited, detonating");
                                        Detonate(); //nuke engine off after comp start, detonate.
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        void CheckAttached(Vessel v)
        {
            if (v != vessel || hasDetonated || goingCritical || !engineCore) return;
            VesselModuleRegistry.OnVesselModified(v);
            if (VesselModuleRegistry.GetModuleCount<MissileFire>(v) == 0)
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.RWPS3R2NukeModule]: Nuclear engine on " + Sourcevessel + " has become detached.");
                goingCritical = true;
                StartCoroutine(DelayedDetonation(0.5f));
            }
        }

        IEnumerator DelayedDetonation(float delay)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.RWPS3R2NukeModule]: Nuclear engine on " + Sourcevessel + " going critical in " + delay.ToString("0.0") + "s.");
            goingCritical = true;
            yield return new WaitForSeconds(delay);
            if (!hasDetonated && part != null) Detonate();
        }

        public void OnDestroy()
        {
            GameEvents.onVesselPartCountChanged.Remove(CheckAttached);
            GameEvents.onVesselCreate.Remove(CheckAttached);
        }

        public void Detonate()
        {
            if (hasDetonated || FlightGlobals.currentMainBody == null || VesselSpawner.Instance.vesselsSpawning) // Don't trigger on scene changes or during spawning.
            {
                return;
            }
            if (missile != null &&
                (missile.MissileState == MissileBase.MissileStates.Idle || missile.MissileState == MissileBase.MissileStates.Drop))
            {
                return;
            }
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDArmory.BDModuleNuke]: Running Detonate() on nukeModule in vessel " + Sourcevessel);
            //affect any nearby parts/vessels that aren't the source vessel
            NukeFX.CreateExplosion(part.transform.position, ExplosionSourceType.BattleDamage, Sourcevessel, 0, thermalRadius, yield, fluence, isEMP, reportingName, "", blastSoundPath, blastSoundPath, flashModelPath, shockModelPath, blastModelPath, plumeModelPath, debrisModelPath);
            hasDetonated = true;
            if (part.vessel != null) // Already in the process of being destroyed.
                part.Destroy();
        }
        public override string GetInfo()
        {
            StringBuilder output = new StringBuilder();
            if (engineCore)
            {
                output.Append(Environment.NewLine);
                output.AppendLine($"Reactor Core");
                output.AppendLine($"An unstable reactor core that will detonate if the engine is disabled");
                output.AppendLine($"Yield: {yield}");
                output.AppendLine($"Generates EMP: {isEMP}");
            }
            if (missile == null)
            {
                output.AppendLine($"Nuclear Warhead");
                output.AppendLine($"Yield: {yield}");
                output.AppendLine($"Generates EMP: {isEMP}");
            }
            return output.ToString();
        }
    }
}
