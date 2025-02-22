﻿using BDArmory.Core;
using BDArmory.Control;
using BDArmory.Misc;
using System.Collections.Generic;
using UnityEngine;
using BDArmory.Core.Extension;
using BDArmory.Core.Module;
using BDArmory.UI;
using BDArmory.FX;
using System.Linq;
using BDArmory.Bullets;

namespace BDArmory.Modules
{
    class BDAMutator : PartModule
    {
        float startTime;
        bool mutatorEnabled = false;
        public List<string> mutators;
        private MutatorInfo mutatorInfo;

        public string mutatorName;
        private float Vampirism = 0;
        private float Regen = 0;
        private float engineMult = 1;

        private bool Vengeance = false;
        private List<string> ResourceTax;
        private double TaxRate = 0;
        private bool hasTaxes;
        private int oldScore = 0;
        bool applyVampirism = false;
        private float Accumulator;

        private string iconPath;
        private string iconcolor;
        private Color iconColor;
        private Texture2D icon;
        public Material IconMat;

        private int ActiveMutators = 1;

        public override void OnStart(StartState state)
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                part.force_activate();
            }
            base.OnStart(state);
        }


        public void EnableMutator(string name = "def") //FIXME - when using apply on timer and !apply global, this NREs
        {
            if (mutatorEnabled) //replace current mutator with new one
            {
                DisableMutator();
            }
            if (name == "def") //mutator not specified, randomly choose from selected mutators
            {
                var indices = Enumerable.Range(0, BDArmorySettings.MUTATOR_LIST.Count).ToList();
                indices.Shuffle();
                name = string.Join("; ", indices.Take(BDArmorySettings.MUTATOR_APPLY_NUM).Select(i => MutatorInfo.mutators[BDArmorySettings.MUTATOR_LIST[i]].name));
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDAMutator] random mutator list built: " + name + " on " + part.vessel.GetName());
            }
            mutatorName = name;
            mutators = BDAcTools.ParseNames(name);
            for (int r = 0; r < BDArmorySettings.MUTATOR_APPLY_NUM; r++)
            {
                name = MutatorInfo.mutators[mutators[r]].name;
                mutatorInfo = MutatorInfo.mutators[name];
                if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDAMutator] beginning mutator initialization of " + name + " on " + part.vessel.GetName());

                if (mutatorInfo.weaponMod)
                {
                    using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                        while (weapon.MoveNext())
                        {
                            if (weapon.Current == null) continue;
                            if (mutatorInfo.weaponType != "def")
                            {
                                weapon.Current.ParseWeaponType(mutatorInfo.weaponType);
                            }
                            if (mutatorInfo.bulletType != "def")
                            {
                                weapon.Current.currentType = mutatorInfo.bulletType;
                                weapon.Current.useCustomBelt = false;
                                weapon.Current.SetupBulletPool();
                                weapon.Current.ParseAmmoStats();
                            }

                            if (mutatorInfo.RoF > 0)
                            {
                                weapon.Current.roundsPerMinute = mutatorInfo.RoF;
                            }
                            if (mutatorInfo.MaxDeviation > 0)
                            {
                                weapon.Current.maxDeviation = mutatorInfo.MaxDeviation;
                            }
                            if (mutatorInfo.laserDamage > 0)
                            {
                                weapon.Current.laserDamage = mutatorInfo.laserDamage;
                            }
                            if (mutatorInfo.instaGib)
                            {
                                weapon.Current.instagib = mutatorInfo.instaGib;
                            }
                            else
                            {
                                if (weapon.Current.strengthMutator > 0)
                                {
                                    weapon.Current.strengthMutator = weapon.Current.strengthMutator;
                                }
                            }
                            if (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Laser)
                            {
                                if (!string.IsNullOrEmpty(mutatorInfo.bulletType) || mutatorInfo.bulletType != "def")
                                {
                                    weapon.Current.projectileColor = BulletInfo.bullets[mutatorInfo.bulletType].projectileColor;
                                }
                                else
                                {
                                    var WM = VesselModuleRegistry.GetMissileFire(part.vessel, true);
                                    string color = $"{Mathf.RoundToInt(BDTISetup.Instance.ColorAssignments[WM.Team.Name].r * 255)},{Mathf.RoundToInt(BDTISetup.Instance.ColorAssignments[WM.Team.Name].g * 255)},{Mathf.RoundToInt(BDTISetup.Instance.ColorAssignments[WM.Team.Name].b * 255)},{Mathf.RoundToInt(BDTISetup.Instance.ColorAssignments[WM.Team.Name].a * 255)}";
                                    weapon.Current.projectileColor = color;
                                }
                                weapon.Current.SetupLaserSpecifics();
                                weapon.Current.pulseLaser = true;
                            }
                            if (weapon.Current.eWeaponType == ModuleWeapon.WeaponTypes.Rocket && weapon.Current.weaponType != "rocket")
                            {
                                weapon.Current.rocketPod = false;
                                weapon.Current.externalAmmo = true;
                            }
                            weapon.Current.resourceSteal = mutatorInfo.resourceSteal;
                            weapon.Current.impulseWeapon = false;
                            weapon.Current.graviticWeapon = false;
                            //Debug.Log("[MUTATOR] current weapon status: " + weapon.Current.WeaponStatusdebug());
                        }
                }
                if (mutatorInfo.EngineMult > 0)
                {
                    engineMult *= mutatorInfo.EngineMult;
                }
                if (mutatorInfo.Vampirism > 0)
                {
                    Vampirism = mutatorInfo.Vampirism;
                }
                if (mutatorInfo.Regen != 0)
                {
                    Regen = mutatorInfo.Regen;
                }
                using (List<Part>.Enumerator part = vessel.Parts.GetEnumerator())
                    while (part.MoveNext())
                    {
                        if (mutatorInfo.Defense > 0)
                        {
                            var HPT = part.Current.FindModuleImplementing<HitpointTracker>();
                            HPT.defenseMutator = mutatorInfo.Defense;
                        }
                        if (mutatorInfo.MassMod != 0)
                        {
                            var MM = part.Current.FindModuleImplementing<ModuleMassAdjust>();
                            if (MM == null)
                            {
                                MM = (ModuleMassAdjust)part.Current.AddModule("ModuleMassAdjust");
                                if (BDArmorySettings.MUTATOR_DURATION > 0 && BDArmorySettings.MUTATOR_APPLY_TIMER)
                                {
                                    MM.duration = BDArmorySettings.MUTATOR_DURATION; //MMA will time out and remove itself when mutator expires
                                }
                                else
                                {
                                    MM.duration = BDArmorySettings.COMPETITION_DURATION;
                                }
                                MM.massMod = mutatorInfo.MassMod / vessel.Parts.Count; //evenly distribute mass change across entire vessel
                            }
                        }
                    }
                if (!Vengeance && mutatorInfo.Vengeance)
                {
                    Vengeance = mutatorInfo.Vengeance;
                }
                if (Vengeance)
                {
                    part.OnJustAboutToBeDestroyed += Detonate;
                }
                if (!string.IsNullOrEmpty(mutatorInfo.resourceTax))
                {
                    hasTaxes = true;
                }
            }
            if (engineMult != 1)
            {
                using (var engine = VesselModuleRegistry.GetModuleEngines(vessel).GetEnumerator())
                    while (engine.MoveNext())
                    {
                        engine.Current.thrustPercentage *= engineMult;
                    }
            }
            startTime = Time.time;
            if (IconMat == null)
            {
                IconMat = new Material(Shader.Find("KSP/Particles/Alpha Blended"));
            }
            ActiveMutators = BDArmorySettings.MUTATOR_APPLY_NUM;
            if (ActiveMutators < 1)
            {
                ActiveMutators = 1;
            }
            mutatorEnabled = true;
        }

        public void DisableMutator()
        {
            if (!mutatorEnabled) return;
            mutatorEnabled = false;
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDAMutator]: Disabling " + mutatorInfo.name + "Mutator on " + part.vessel.vesselName);
            using (var weapon = VesselModuleRegistry.GetModules<ModuleWeapon>(vessel).GetEnumerator())
                while (weapon.MoveNext())
                {
                    if (weapon.Current == null) continue;
                    weapon.Current.ParseWeaponType(weapon.Current.weaponType);
                    if (!string.IsNullOrEmpty(weapon.Current.ammoBelt) && weapon.Current.ammoBelt != "def")
                    {
                        weapon.Current.useCustomBelt = true;
                    }
                    weapon.Current.roundsPerMinute = weapon.Current.baseRPM;
                    weapon.Current.maxDeviation = weapon.Current.baseDeviation;
                    weapon.Current.laserDamage = weapon.Current.baseLaserdamage;
                    weapon.Current.pulseLaser = weapon.Current.pulseInConfig;
                    weapon.Current.impulseWeapon = weapon.Current.ImpulseInConfig;
                    weapon.Current.graviticWeapon = weapon.Current.GraviticInConfig;
                    weapon.Current.instagib = false;
                    weapon.Current.strengthMutator = 1;
                    weapon.Current.SetupAmmo(null, null);
                    weapon.Current.resourceSteal = false;
                }
            if (engineMult != 1)
            {
                using (var engine = VesselModuleRegistry.GetModuleEngines(vessel).GetEnumerator())
                    while (engine.MoveNext())
                    {
                        engine.Current.thrustPercentage /= engineMult;
                    }
            }
            engineMult = 1; //changing this to 1 from 0, this makes sure that there isn't a multiply by 0 issue with if later calling EnableMutator on a mutator with an engine mult
            Vampirism = 0;
            Regen = 0;
            using (List<Part>.Enumerator part = vessel.Parts.GetEnumerator())
                while (part.MoveNext())
                {
                    var HPT = part.Current.FindModuleImplementing<HitpointTracker>();
                    HPT.defenseMutator = 1;
                }
            if (Vengeance)
            {
                Vengeance = false;
                part.OnJustAboutToBeDestroyed -= Detonate;
            }
            List<string> ResourceTax = new List<string>();
            TaxRate = 0;
            hasTaxes = false;
        }

        void Update()
        {
            if (HighLogic.LoadedSceneIsFlight && !BDArmorySetup.GameIsPaused && !vessel.packed)
            {
                if (!mutatorEnabled) return;

                if ((BDArmorySettings.MUTATOR_DURATION > 0 && Time.time - startTime > BDArmorySettings.MUTATOR_DURATION * 60) && (BDArmorySettings.MUTATOR_APPLY_TIMER || BDArmorySettings.MUTATOR_APPLY_KILL))
                {
                    DisableMutator();
                }
                if (BDACompetitionMode.Instance.Scores.ScoreData.ContainsKey(vessel.vesselName))
                {
                    if (BDACompetitionMode.Instance.Scores.ScoreData[vessel.vesselName].hits > oldScore) //apply HP gain every time a hit is scored
                    {
                        oldScore = BDACompetitionMode.Instance.Scores.ScoreData[vessel.vesselName].hits;
                        applyVampirism = true;
                    }
                }
                if (Regen != 0 || Vampirism > 0)
                {
                    using (List<Part>.Enumerator part = vessel.Parts.GetEnumerator())
                        while (part.MoveNext())
                        {
                            if (Regen != 0 && Accumulator > 5) //Add regen HP every 5 seconds
                            {
                                part.Current.AddHealth(Regen, Vampirism > 0); //don't clamp HP to default if vampirism also enabled to prevent regen resetting gained HP from vampirism
                            }
                            if (Vampirism > 0 && applyVampirism)
                            {
                                part.Current.AddHealth(Vampirism, true);
                            }
                        }
                }
                applyVampirism = false;
                //To allow multiple tax mutators, but have it so resources are taxed per mutator - i.e. Mut1 has ammo regen, mut2 has fueltax, and both proc
                if (hasTaxes && Accumulator > 5) //Apply resource tax once every 5 seconds)
                {
                    for (int r = 0; r < ActiveMutators; r++)
                    {
                        TaxRate = MutatorInfo.mutators[mutators[r]].resourceTaxRate;
                        if (TaxRate != 0)
                        {
                            try
                            {
                                ResourceTax = BDAcTools.ParseNames(MutatorInfo.mutators[mutators[r]].resourceTax);
                                int Tax = ResourceTax.Count;
                                if (Tax >= 1)
                                {
                                    for (int i = 0; i < Tax; i++)
                                    {
                                        part.RequestResource(ResourceTax[i], TaxRate, ResourceFlowMode.ALL_VESSEL);
                                    }
                                }
                            }
                            catch
                            {
                                Debug.Log("[BDAMutator] mutator not configured correctly. Set ResourceTaxRate to 0 or add resource to ResourceTax");
                            }
                        }
                    }
                }
                if (Regen != 0 || TaxRate != 0)
                {
                    if (Accumulator > 5)
                    {
                        Accumulator = 0;
                    }
                    else
                    {
                        Accumulator += TimeWarp.fixedDeltaTime;
                    }
                }
                
            }
        }
        void OnGUI() 
        {
            if (Event.current.type.Equals(EventType.Repaint))
            {
                if (HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDArmorySettings.MUTATOR_ICONS)
                {
                    if (mutatorEnabled)
                    {
                        Vector3 screenPos = BDGUIUtils.GetMainCamera().WorldToViewportPoint(vessel.CoM);
                        if (screenPos.z < 0) return; //dont draw if point is behind camera
                        if (screenPos.x != Mathf.Clamp01(screenPos.x)) return; //dont draw if off screen
                        if (screenPos.y != Mathf.Clamp01(screenPos.y)) return;
                        float yPos = ((1 - screenPos.y) * Screen.height) - (0.5f * (30 * BDTISettings.ICONSCALE)) - (30 * BDTISettings.ICONSCALE);

                        for (int i = 0; i < ActiveMutators; i++)
                        {
                            float xPos = (screenPos.x * Screen.width) - (0.5f * 30 * BDTISettings.ICONSCALE) - ((ActiveMutators - 1) * 0.5f * 30 * BDTISettings.ICONSCALE);
                            Rect iconRect = new Rect(xPos + (i * 30 * BDTISettings.ICONSCALE), yPos, (30 * BDTISettings.ICONSCALE), (30 * BDTISettings.ICONSCALE));

                            iconPath = MutatorInfo.mutators[mutators[i]].icon;
                            iconcolor = MutatorInfo.mutators[mutators[i]].iconColor;
                            iconColor = Misc.Misc.ParseColor255(iconcolor);
                            switch (iconPath)
                            {
                                case "IconAccuracy":
                                    icon = BDTISetup.Instance.MutatorIconAcc;
                                    break;
                                case "IconAttack":
                                    icon = BDTISetup.Instance.MutatorIconAtk;
                                    break;
                                case "IconAttack2":
                                    icon = BDTISetup.Instance.MutatorIconAtk2;
                                    break;
                                case "IconBallistic":
                                    icon = BDTISetup.Instance.MutatorIconBullet;
                                    break;
                                case "IconDefense":
                                    icon = BDTISetup.Instance.MutatorIconDefense;
                                    break;
                                case "IconLaser":
                                    icon = BDTISetup.Instance.MutatorIconLaser;
                                    break;
                                case "IconMass":
                                    icon = BDTISetup.Instance.MutatorIconMass;
                                    break;
                                case "IconRegen":
                                    icon = BDTISetup.Instance.MutatorIconRegen;
                                    break;
                                case "IconRocket":
                                    icon = BDTISetup.Instance.MutatorIconRocket;
                                    break;
                                case "IconSkull":
                                    icon = BDTISetup.Instance.MutatorIconDoom;
                                    break;
                                case "IconSpeed":
                                    icon = BDTISetup.Instance.MutatorIconSpeed;
                                    break;
                                case "IconTarget":
                                    icon = BDTISetup.Instance.MutatorIconTarget;
                                    break;
                                case "IconVampire":
                                    icon = BDTISetup.Instance.MutatorIconVampire;
                                    break;
                                case "IconUnknown":
                                    icon = BDTISetup.Instance.MutatorIconNull;
                                    break;
                                default: // Other?
                                    icon = BDTISetup.Instance.MutatorIconNull;
                                    break;
                            }
                            if (icon != null)
                            {
                                //GUI.DrawTexture(iconRect, icon);

                                IconMat.SetColor("_TintColor", iconColor);
                                IconMat.mainTexture = icon;
                                Graphics.DrawTexture(iconRect, icon, IconMat);
                            }
                        }
                    }
                }
            }
        }
        void Detonate() 
        {
            if (!Vengeance) return;
            if (!BDACompetitionMode.Instance.competitionIsActive) return;
            if (BDArmorySettings.DRAW_DEBUG_LABELS) Debug.Log("[BDAMutator] triggering vengeance nuke");
            NukeFX.CreateExplosion(part.transform.position, ExplosionSourceType.BattleDamage, this.vessel.GetName(), 2.5f, 500, 0.05f, 0.05f, true, "Vengeance Explosion", "BDArmory/Models/Mutators/Vengence");
        }
    }
}

