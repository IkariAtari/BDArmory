﻿﻿using System;
using System.Collections.Generic;
using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Module;
using BDArmory.FX;
using BDArmory.Misc;
using BDArmory.Modules;
using BDArmory.UI;
using UniLinq;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class PooledRocket : MonoBehaviour
    {
        public RocketInfo rocket; //get tracers, expFX urls moved to BulletInfo
                                  //Seeker/homing rocket code (Image-Recognition tracking?)

        public Transform spawnTransform;
        public Vessel sourceVessel;
        public string sourceVesselName;
        public string team;
        public string rocketName;
        public float rocketMass;
        public float caliber;
        public float thrust;
        private Vector3 thrustVector;
        private Vector3 dragVector;
        public float thrustTime;
        public bool shaped;
        public float maxAirDetonationRange;
        public bool flak;
        public bool concussion;
        public bool gravitic;
        public bool EMP;
        public bool choker;
        public bool thief;
        public float massMod = 0;
        public float impulse = 0;
        public bool incendiary;
        public float detonationRange;
        public float tntMass;
        bool explosive = true;
        public float bulletDmgMult = 1;
        public float dmgMult = 1;
        public float blastRadius = 0;
        public float randomThrustDeviation = 0.05f;
        public float massScalar = 0.012f;
        private float HERatio = 0.1f;
        public string explModelPath;
        public string explSoundPath;

        public string rocketSoundPath;

        float startTime;
        float stayTime = 0.04f;
        float lifeTime = 10;

        Vector3 prevPosition;
        public Vector3 currPosition;
        Vector3 startPosition;
        bool startUnderwater = false;
        Ray RocketRay;
        private float impactVelocity;
        public Vector3 currentVelocity = Vector3.zero;

        public bool hasPenetrated = false;
        public bool hasDetonated = false;
        public int penTicker = 0;
        private Part CurrentPart = null;

        private float distanceFromStart = 0;

        //bool isThrusting = true;
        public bool isAPSprojectile = false;
        public PooledRocket tgtRocket = null;
        public PooledBullet tgtShell = null;

        Rigidbody rb;
        public Rigidbody parentRB;

        KSPParticleEmitter[] pEmitters;
        BDAGaplessParticleEmitter[] gpEmitters;

        float randThrustSeed;

        public AudioSource audioSource;

        HashSet<Vessel> craftHit = new HashSet<Vessel>();
        void OnEnable()
        {
            BDArmorySetup.numberOfParticleEmitters++;

            rb = gameObject.AddOrGetComponent<Rigidbody>();

            pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

            using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                while (pe.MoveNext())
                {
                    if (pe.Current == null) continue;
                    if (FlightGlobals.getStaticPressure(transform.position) == 0 && pe.Current.useWorldSpace)
                    {
                        pe.Current.emit = false;
                    }

                    else if (pe.Current.useWorldSpace)
                    {
                        BDAGaplessParticleEmitter gpe = pe.Current.gameObject.AddComponent<BDAGaplessParticleEmitter>();
                        gpe.rb = rb;
                        gpe.emit = true;
                    }

                    else
                    {
                        pe.Current.emit = true;
                        EffectBehaviour.AddParticleEmitter(pe.Current);
                    }
                }
            gpEmitters = gameObject.GetComponentsInChildren<BDAGaplessParticleEmitter>();

            prevPosition = transform.position;
            currPosition = transform.position;
            startPosition = transform.position;
            startTime = Time.time;
            if (FlightGlobals.getAltitudeAtPos(transform.position) < 0)
            {
                startUnderwater = true;
            }
            else
                startUnderwater = false;
            massScalar = 0.012f / rocketMass;

            rb.mass = rocketMass;
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            currentVelocity = Vector3.zero;
            if (!FlightGlobals.RefFrameIsRotating) rb.useGravity = false;

            rb.useGravity = false;

            randThrustSeed = UnityEngine.Random.Range(0f, 100f);
            thrustVector = new Vector3(0, 0, thrust);
            dragVector = new Vector3();
            SetupAudio();

            // Log rockets fired.
            if (this.sourceVessel)
            {
                sourceVesselName = sourceVessel.GetName(); // Set the source vessel name as the vessel might have changed its name or died by the time the rocket hits.
                BDACompetitionMode.Instance.Scores.RegisterRocketFired(sourceVesselName);
            }
            else
            {
                sourceVesselName = null;
            }
            if (tntMass <= 0)
            {
                explosive = false;
            }
            if (explosive)
            {
                HERatio = Mathf.Clamp(tntMass / ((rocketMass * 1000) < tntMass ? tntMass * 1.25f : (rocketMass * 1000)), 0.01f, 0.95f);
            }
            else
            {
                HERatio = 0;
            }
            if (caliber > 60)
            {
                BDATargetManager.FiredRockets.Add(this);
            }
        }

        void OnDisable()
        {
            BDArmorySetup.OnVolumeChange -= UpdateVolume;
            BDArmorySetup.numberOfParticleEmitters--;
            foreach (var gpe in gpEmitters)
                if (gpe != null)
                {
                    gpe.emit = false;
                }
            foreach (var pe in pEmitters)
                if (pe != null)
                {
                    pe.emit = false;
                    EffectBehaviour.RemoveParticleEmitter(pe);
                }
            sourceVessel = null;
            sourceVesselName = null;
            spawnTransform = null;
            CurrentPart = null;
            if (caliber > 60)
            {
                BDATargetManager.FiredRockets.Remove(this);
            }
            isAPSprojectile = false;
            tgtRocket = null;
            tgtShell = null;
        }

        void FixedUpdate()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            //floating origin and velocity offloading corrections
            if (!FloatingOrigin.Offset.IsZero() || !Krakensbane.GetFrameVelocity().IsZero())
            {
                transform.position -= FloatingOrigin.OffsetNonKrakensbane;
                prevPosition -= FloatingOrigin.OffsetNonKrakensbane;
                startPosition -= FloatingOrigin.OffsetNonKrakensbane;
            }
            distanceFromStart = Vector3.Distance(transform.position, startPosition);

            if (Time.time - startTime < stayTime && transform.parent != null)
            {
                transform.rotation = transform.parent.rotation;
                transform.position = spawnTransform.position;
                //+(transform.parent.rigidbody.velocity*Time.fixedDeltaTime);
            }
            else
            {
                if (transform.parent != null && parentRB)
                {
                    transform.parent = null;
                    rb.isKinematic = false;
                    rb.velocity = parentRB.velocity + Krakensbane.GetFrameVelocityV3f();
                }
            }

            if (rb && !rb.isKinematic)
            {
                //physics
                if (FlightGlobals.RefFrameIsRotating)
                {
                    rb.velocity += FlightGlobals.getGeeForceAtPosition(transform.position) * Time.fixedDeltaTime;
                }

                //guidance and attitude stabilisation scales to atmospheric density.
                float atmosMultiplier =
                    Mathf.Clamp01((float)FlightGlobals.getAtmDensity(FlightGlobals.getStaticPressure(transform.position),
                                      FlightGlobals.getExternalTemperature(), FlightGlobals.currentMainBody));

                //model transform. always points prograde
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(rb.velocity + Krakensbane.GetFrameVelocity(), transform.up),
                    Mathf.Clamp01(atmosMultiplier * 2.5f) * (0.5f * (Time.time - startTime)) * 50 * Time.fixedDeltaTime);


                if (Time.time - startTime < thrustTime && Time.time - startTime > stayTime)
                {
                    thrustVector.x = randomThrustDeviation * (1 - (Mathf.PerlinNoise(4 * Time.time, randThrustSeed) * 2)) / massScalar;//this needs to scale w/ rocket mass, or light projectiles will be 
                    thrustVector.y = randomThrustDeviation * (1 - (Mathf.PerlinNoise(randThrustSeed, 4 * Time.time) * 2)) / massScalar;//far more affected than heavier ones
                    rb.AddRelativeForce(thrustVector);
                }//0.012/rocketmass - use .012 as baseline, it's the mass of the hydra, which the randomTurstdeviation was originally calibrated for
                if (BDArmorySettings.BULLET_WATER_DRAG)
                {
                    if (FlightGlobals.getAltitudeAtPos(currPosition) < 0)
                    {
                        //atmosMultiplier *= 83.33f;
                        dragVector.z = -(0.5f * 1 * (rb.velocity.magnitude * rb.velocity.magnitude) * 0.5f * ((Mathf.PI * caliber * caliber * 0.25f) / 1000000)) * TimeWarp.fixedDeltaTime;
                        rb.AddRelativeForce(dragVector); //this is going to throw off aiming code, but you aren't going to hit anything with rockets underwater anyway
                    }
                    //dragVector.z = -(0.5f * (atmosMultiplier * 0.012f) * (rb.velocity.magnitude * rb.velocity.magnitude) * 0.5f * ((Mathf.PI * caliber * caliber * 0.25f) / 1000000))*TimeWarp.fixedDeltaTime;
                    //rb.AddRelativeForce(dragVector);
                    //Debug.Log("[ROCKETDRAG] current vel: " + rb.velocity.magnitude.ToString("0.0") + "; current dragforce: " + dragVector.magnitude + "; current atm density: " + atmosMultiplier.ToString("0.00"));
                }
                currentVelocity = rb.velocity;
            }

            if (Time.time - startTime > thrustTime)
            {
                using (var pe = pEmitters.AsEnumerable().GetEnumerator())
                    while (pe.MoveNext())
                    {
                        if (pe.Current == null) continue;
                        pe.Current.emit = false;
                    }
                using (var gpe = gpEmitters.AsEnumerable().GetEnumerator())
                    while (gpe.MoveNext())
                    {
                        if (gpe.Current == null) continue;
                        gpe.Current.emit = false;
                    }
                if (audioSource)
                {
                    audioSource.loop = false;
                    audioSource.Stop();
                }
            }
            if (Time.time - startTime > 0.1f + stayTime)
            {
                hasPenetrated = true;
                hasDetonated = false;
                penTicker = 0;

                currPosition = transform.position;
                float dist = (currPosition - prevPosition).magnitude;
                RocketRay = new Ray(prevPosition, currPosition - prevPosition);
                var hits = Physics.RaycastAll(RocketRay, dist, 9076737);
                if (hits.Length > 0)
                {
                    var orderedHits = hits.OrderBy(x => x.distance);

                    using (var hitsEnu = orderedHits.GetEnumerator())
                    {
                        while (hitsEnu.MoveNext())
                        {
                            if (!hasPenetrated || hasDetonated) break;

                            RaycastHit hit = hitsEnu.Current;
                            Part hitPart = null;
                            KerbalEVA hitEVA = null;

                            try
                            {
                                hitPart = hit.collider.gameObject.GetComponentInParent<Part>();
                                hitEVA = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                            }
                            catch (NullReferenceException e)
                            {
                                Debug.LogWarning("[BDArmory.BDArmory]:NullReferenceException for Kinetic Hit: " + e.Message);
                                return;
                            }

                            if (hitPart != null && ProjectileUtils.IsIgnoredPart(hitPart)) continue; // Ignore ignored parts.
                            if (hitPart != null && (hitPart == CurrentPart && CurrentPart.name.Contains("Armor"))) continue; //only have bullet hit armor panels once - no back armor to hit if penetration
                            CurrentPart = hitPart;
                            if (hitEVA != null)
                            {
                                hitPart = hitEVA.part;
                                // relative velocity, separate from the below statement, because the hitpart might be assigned only above
                                if (hitPart.rb != null)
                                    impactVelocity = (rb.velocity - (hitPart.rb.velocity + Krakensbane.GetFrameVelocityV3f())).magnitude;
                                else
                                    impactVelocity = rb.velocity.magnitude;
                                if (dmgMult < 0)
                                {
                                    hitPart.AddInstagibDamage();
                                }
                                else
                                {
                                    ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, 1, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team, ExplosionSourceType.Rocket, true, true, true);
                                }
                                ProjectileUtils.StealResources(hitPart, sourceVessel, thief);
                                Detonate(hit.point, false);
                                return;
                            }

                            if (hitPart != null && hitPart.vessel == sourceVessel) continue;  //avoid autohit;

                            Vector3 impactVector = rb.velocity;
                            if (hitPart != null && hitPart.rb != null)
                                // using relative velocity vector instead of just rocket velocity
                                // since KSP vessels can easily be moving faster than rockets
                                impactVector = rb.velocity - (hitPart.rb.velocity + Krakensbane.GetFrameVelocityV3f());

                            float hitAngle = Vector3.Angle(impactVector, -hit.normal);

                            if (ProjectileUtils.CheckGroundHit(hitPart, hit, caliber))
                            {
                                ProjectileUtils.CheckBuildingHit(hit, rocketMass * 1000, rb.velocity, bulletDmgMult);
                                Detonate(hit.point, false);
                                return;
                            }

                            impactVelocity = impactVector.magnitude;
                            if (gravitic)
                            {
                                var ME = hitPart.FindModuleImplementing<ModuleMassAdjust>();
                                if (ME == null)
                                {
                                    ME = (ModuleMassAdjust)hitPart.AddModule("ModuleMassAdjust");
                                }
                                ME.massMod += massMod;
                                ME.duration += BDArmorySettings.WEAPON_FX_DURATION;
                            }
                            if (concussion && hitPart.rb != null || BDArmorySettings.PAINTBALL_MODE)
                            {
                                if (concussion && hitPart.rb != null)
                                        {
                                    hitPart.rb.AddForceAtPosition(impactVector.normalized * impulse, hit.point, ForceMode.Acceleration);
                                }
                                BDACompetitionMode.Instance.Scores.RegisterRocketStrike(sourceVesselName, hitPart.vessel.GetName());
                                Detonate(hit.point, false);
                                hasDetonated = true;
                                return; //impulse rounds shouldn't penetrate/do damage
                            }
                            float anglemultiplier = (float)Math.Cos(Math.PI * hitAngle / 180.0);

                            float thickness = ProjectileUtils.CalculateThickness(hitPart, anglemultiplier);
                            float penetration = 0;
                            float penetrationFactor = 0;
                            var Armor = hitPart.FindModuleImplementing<HitpointTracker>();
                            if (Armor != null)
                            {
                                float Ductility = Armor.Ductility;
                                float hardness = Armor.Hardness;
                                float Strength = Armor.Strength;
                                float safeTemp = Armor.SafeUseTemp;
                                float Density = Armor.Density;
                                int armorType = (int)Armor.ArmorTypeNum;
                                if (BDArmorySettings.DRAW_ARMOR_LABELS)
                                {
                                    Debug.Log("[PooledBUllet].ArmorVars found: Strength : " + Strength + "; Ductility: " + Ductility + "; Hardness: " + hardness + "; MaxTemp: " + safeTemp + "; Density: " + Density);
                                }
                                float bulletEnergy = ProjectileUtils.CalculateProjectileEnergy(rocketMass * 1000, impactVelocity);
                                float armorStrength = ProjectileUtils.CalculateArmorStrength(caliber, thickness, Ductility, Strength, Density, safeTemp, hitPart);
                                //calculate bullet deformation
                                float newCaliber = ProjectileUtils.CalculateDeformation(armorStrength, bulletEnergy, caliber, impactVelocity, hardness, Density, HERatio, 1);
                                //calculate penetration
                                penetration = ProjectileUtils.CalculatePenetration(caliber, newCaliber, rocketMass * 1000, impactVelocity, Ductility, Density, Strength, thickness, 1);
                                caliber = newCaliber; //update bullet with new caliber post-deformation(if any)
                                penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness);

                                var RA = hitPart.FindModuleImplementing<ModuleReactiveArmor>();
                                if (RA != null)
                                {
                                    if (penetrationFactor > 1)
                                    {
                                        float thicknessModifier = RA.armorModifier;
                                        {
                                            if (RA.NXRA) //non-explosive RA, always active
                                            {
                                                thickness *= thicknessModifier;
                                            }
                                            else
                                            {
                                                if (caliber >= RA.sensitivity) //big enough round to trigger RA
                                                {
                                                    thickness *= thicknessModifier;
                                                    if (tntMass <= 0) //non-explosive impact
                                                    {
                                                        RA.UpdateSectionScales(); //detonate RA section
                                                                                  //explosive impacts handled in ExplosionFX
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    penetrationFactor = ProjectileUtils.CalculateArmorPenetration(hitPart, penetration, thickness); //RA stop round?
                                }
                                else ProjectileUtils.CalculateArmorDamage(hitPart, penetrationFactor, caliber, hardness, Ductility, Density, impactVelocity, sourceVessel.GetName(), ExplosionSourceType.Rocket, armorType);

                                //calculate return bullet post-pen vel
                                //calculate armor damage
                                //FIXME later - if doing bullet style armor penetrtion, then immplement armor penetration, and let AP/kinetic warhead rockets (over?)penetrate parts
                            }
                            else
                            {
                                Debug.Log("[PooledBUllet].ArmorVars not found; hitPart null");
                            }
                            if (penetration > thickness)
                            {
                                rb.velocity = rb.velocity * (float)Math.Sqrt(thickness / penetration);
                                if (penTicker > 0) rb.velocity *= 0.55f;
                            }

                            if (penetrationFactor > 1)
                            {
                                hasPenetrated = true;

                                bool viableBullet = ProjectileUtils.CalculateBulletStatus(rocketMass * 1000, caliber);
                                if (dmgMult < 0)
                                {
                                    hitPart.AddInstagibDamage();
                                }
                                else
                                {
                                    float cockpitPen = (float)(16f * impactVelocity * Mathf.Sqrt(rocketMass) / Mathf.Sqrt(caliber));
                                    if (cockpitPen > Mathf.Max(20 / anglemultiplier, 1))
                                    ProjectileUtils.ApplyDamage(hitPart, hit, dmgMult, penetrationFactor, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team, ExplosionSourceType.Rocket, penTicker > 0 ? false : true, penTicker > 0 ? false : true, (cockpitPen > Mathf.Max(20 / anglemultiplier, 1)) ? true : false);
                                    if (!explosive)
                                    {
                                        BDACompetitionMode.Instance.Scores.RegisterRocketStrike(sourceVesselName, hitPart.vessel.GetName()); //if non-explosive hit, add rocketstrike, else ExplosionFX adds rocketstrike from HE detonation
                                    }
                                }
                                ProjectileUtils.StealResources(hitPart, sourceVessel, thief);

                                penTicker += 1;
                                ProjectileUtils.CheckPartForExplosion(hitPart);

                                if (explosive || !viableBullet)
                                {
                                    transform.position += (rb.velocity * Time.fixedDeltaTime) / 3;

                                    Detonate(transform.position, false);
                                    hasDetonated = true;
                                }
                            }
                            else // stopped by armor
                            {
                                if (hitPart.rb != null && hitPart.rb.mass > 0)
                                {
                                    float forceAverageMagnitude = impactVelocity * impactVelocity *
                                                          (1f / hit.distance) * (rocketMass * 1000);

                                    float accelerationMagnitude =
                                        forceAverageMagnitude / (hitPart.vessel.GetTotalMass() * 1000);

                                    hitPart.rb.AddForceAtPosition(impactVector.normalized * accelerationMagnitude, hit.point, ForceMode.Acceleration);

                                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                        Debug.Log("[BDArmory.PooledRocket]: Force Applied " + Math.Round(accelerationMagnitude, 2) + "| Vessel mass in kgs=" + hitPart.vessel.GetTotalMass() * 1000 + "| rocket effective mass =" + rocketMass * 1000);
                                }

                                hasPenetrated = false;
                                //ProjectileUtils.ApplyDamage(hitPart, hit, 1, penetrationFactor, caliber, rocketMass * 1000, impactVelocity, bulletDmgMult, distanceFromStart, explosive, incendiary, false, sourceVessel, rocketName, team);
                                //not going to do ballistic damage if stopped by armor
                                ProjectileUtils.CalculateShrapnelDamage(hitPart, hit, caliber, tntMass, 0, sourceVesselName, ExplosionSourceType.Rocket, (rocketMass * 1000), penetrationFactor);
                                //the warhead exploding, on the other hand...
                                Detonate(hit.point, false);
                                hasDetonated = true;
                            }

                            if (penTicker >= 2)
                            {
                                Detonate(hit.point, false);
                                return;
                            }

                            if (rb.velocity.magnitude <= 100 && hasPenetrated && (Time.time - startTime > thrustTime))
                            {
                                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                {
                                    Debug.Log("[BDArmory.PooledRocket]: Rocket ballistic velocity too low, stopping");
                                }
                                Detonate(hit.point, false);
                                return;
                            }
                            if (!hasPenetrated || hasDetonated) break;
                        }
                    }
                }
            }
            if (BDArmorySettings.BULLET_WATER_DRAG)
            {
                if (FlightGlobals.getAltitudeAtPos(transform.position) > 0 && startUnderwater)
                {
                    startUnderwater = false;
                    FXMonger.Splash(transform.position, caliber);
                }
                if (FlightGlobals.getAltitudeAtPos(transform.position) <= 0 && !startUnderwater)
                {
                    if (tntMass > 0) //look into fuze options similar to bullets?
                    {
                        Detonate(transform.position, false);
                    }
                    FXMonger.Splash(transform.position, caliber);
                }
            }
            prevPosition = currPosition;

            if (Time.time - startTime > lifeTime) // life's 10s, quite a long time for faster rockets
            {
                Detonate(transform.position, true);
            }
            if (distanceFromStart >= maxAirDetonationRange)//rockets are performance intensive, lets cull those that have flown too far away
            {
                Detonate(transform.position, false);
            }
            if (ProximityAirDetonation(distanceFromStart))
            {
                Detonate(transform.position, false);
            }
        }

        private bool ProximityAirDetonation(float distanceFromStart)
        {
            bool detonate = false;

            if (distanceFromStart <= blastRadius*2) return false;

            if (!explosive || tntMass <= 0) return false;

            if (flak)
            {
                using (var hitsEnu = Physics.OverlapSphere(transform.position, detonationRange, 557057).AsEnumerable().GetEnumerator())
                {
                    while (hitsEnu.MoveNext())
                    {
                        if (hitsEnu.Current == null) continue;
                        try
                        {
                            Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                            if (partHit == null || partHit.vessel == null) continue;
                            if (partHit.vessel == sourceVessel) continue;
                            if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                            var aName = sourceVesselName; //proxi detonated rocket scoring
                            var tName = partHit.vessel.GetName();

                            BDACompetitionMode.Instance.Scores.RegisterRocketHit(aName, tName, 1);

                            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                Debug.Log("[BDArmory.PooledRocket]: rocket proximity sphere hit | Distance overlap = " + detonationRange + "| Part name = " + partHit.name);
                            return detonate = true;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[BDArmory.PooledRocket]: Exception thrown in ProximityAirDetonation: " + e.Message + "\n" + e.StackTrace);
                        }
                    }
                }
                if (isAPSprojectile && (tgtShell != null || tgtRocket != null))
                {
                    if (Vector3.Distance(transform.position, tgtShell != null ? tgtShell.transform.position : tgtRocket.transform.position) < detonationRange/2)
                    {
                        if (BDArmorySettings.DRAW_DEBUG_LABELS)
                            Debug.Log("[BDArmory.PooledRocket]: rocket proximity to APS target | Distance overlap = " + detonationRange + "| tgt name = " + tgtShell != null ? tgtShell.name : tgtRocket.name);
                        return detonate = true;
                    }
                }
            }
            return detonate;
        }

        void Update()
        {
            if (!gameObject.activeInHierarchy)
            {
                return;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                if (BDArmorySetup.GameIsPaused || (Time.time - startTime > thrustTime))
                {
                    if (audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                }
                else
                {
                    if (!audioSource.isPlaying)
                    {
                        audioSource.Play();
                    }
                }
            }
        }

        void Detonate(Vector3 pos, bool missed)
        {
            if (!missed)
            {
                if (tntMass > 0)
                {
                    Vector3 direction = default(Vector3);
                    if (shaped)
                    {
                        direction = (pos + rb.velocity * Time.deltaTime).normalized;
                    }
                    if (gravitic)
                    {
                        using (var hitsEnu = Physics.OverlapSphere(transform.position, blastRadius, 557057).AsEnumerable().GetEnumerator())
                        {
                            while (hitsEnu.MoveNext())
                            {
                                if (hitsEnu.Current == null) continue;

                                Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                                if (partHit == null) continue;
                                if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                float distance = Vector3.Distance(transform.position, partHit.transform.position);
                                if (gravitic)
                                {
                                    if (partHit.mass > 0)
                                    {
                                        var ME = partHit.vessel.rootPart.FindModuleImplementing<ModuleMassAdjust>();
                                        if (ME == null)
                                        {
                                            ME = (ModuleMassAdjust)partHit.vessel.rootPart.AddModule("ModuleMassAdjust");
                                        }
                                        ME.massMod += (massMod * (1 - (distance / blastRadius))); //this way craft at edge of blast might only get disabled instead of bricked
                                        ME.duration += (BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius))); //can bypass EMP damage cap
                                    }
                                }
                            }
                        }
                    }
                    if (incendiary)
                    {
                        for (int f = 0; f < 20; f++) //throw 20 random raytraces out in a sphere and see what gets tagged
                        {
                            Ray LoSRay = new Ray(transform.position, VectorUtils.GaussianDirectionDeviation(transform.forward, 170));
                            RaycastHit hit;
                            if (Physics.Raycast(LoSRay, out hit, blastRadius * 1.2f, 9076737)) // only add fires to parts in LoS of blast
                            {
                                KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
                                Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
                                float distance = Vector3.Distance(transform.position, hit.point);
                                if (p != null)
                                {
                                    BulletHitFX.AttachFire(hit.point, p, caliber, sourceVesselName, BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)), 1, true); //else apply fire to occluding part
                                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                        Debug.Log("[BDArmory.Rocket]: Applying fire to " + p.name + " at distance " + distance + "m, for " + BDArmorySettings.WEAPON_FX_DURATION * (1 - (distance / blastRadius)) + " seconds"); ;
                                }
                            }
                            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                                Debug.Log("[Rocket] incendiary raytrace: " + hit.point.x + "; " + hit.point.y + "; " + hit.point.z);
                        }
                    }
                    if (concussion || EMP || choker)
                    {
                        using (var hitsEnu = Physics.OverlapSphere(transform.position, 25, 557057).AsEnumerable().GetEnumerator())
                        {
                            var craftHit = new HashSet<Vessel>();
                            while (hitsEnu.MoveNext())
                            {
                                if (hitsEnu.Current == null) continue;
                                if (hitsEnu.Current.gameObject == FlightGlobals.currentMainBody.gameObject) continue; // Ignore terrain hits.
                                Part partHit = hitsEnu.Current.GetComponentInParent<Part>();
                                if (partHit == null) continue;
                                if (ProjectileUtils.IsIgnoredPart(partHit)) continue; // Ignore ignored parts.
                                if (craftHit.Contains(partHit.vessel)) continue; // Don't hit the same craft multiple times.
                                craftHit.Add(partHit.vessel);

                                float Distance = Vector3.Distance(partHit.transform.position, this.transform.position);
                                if (partHit != null)
                                {
                                    if (concussion && partHit.mass > 0)
                                    {
                                        partHit.rb.AddForceAtPosition((partHit.transform.position - this.transform.position).normalized * impulse, partHit.transform.position, ForceMode.Acceleration);
                                    }
                                    if (EMP)
                                    {
                                        var MDEC = partHit.vessel.rootPart.FindModuleImplementing<ModuleDrainEC>();
                                        if (MDEC == null)
                                        {
                                            MDEC = (ModuleDrainEC)partHit.vessel.rootPart.AddModule("ModuleDrainEC");
                                        }
                                        MDEC.incomingDamage = ((25 - Distance) * 5); //this way craft at edge of blast might only get disabled instead of bricked
                                        MDEC.softEMP = false; //can bypass EMP damage cap                                            
                                    }
                                    if (choker)
                                    {
                                        var ash = partHit.vessel.rootPart.FindModuleImplementing<ModuleDrainIntakes>();
                                        if (ash == null)
                                        {
                                            ash = (ModuleDrainIntakes)partHit.vessel.rootPart.AddModule("ModuleDrainIntakes");
                                        }
                                        ash.drainDuration += BDArmorySettings.WEAPON_FX_DURATION * (1 - (Distance / 25)); //reduce intake knockout time based on distance from epicenter                                        
                                    }
                                }
                            }
                        }
                        ExplosionFx.CreateExplosion(pos, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Rocket, caliber, null, sourceVesselName, null, direction, -1, true);
                    }
                    else
                    {
                        ExplosionFx.CreateExplosion(pos, tntMass, explModelPath, explSoundPath, ExplosionSourceType.Rocket, caliber, null, sourceVesselName, null, direction, -1, false, rocketMass * 1000, -1, dmgMult, shaped ? "shapedcharge" : "standard");
                    }
                }
            }
            gameObject.SetActive(false);
        }

        void SetupAudio()
        {
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); }
            audioSource.loop = true;
            audioSource.minDistance = 1;
            audioSource.maxDistance = 2000;
            audioSource.dopplerLevel = 0.5f;
            audioSource.volume = 0.9f * BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            audioSource.pitch = 1f;
            audioSource.priority = 255;
            audioSource.spatialBlend = 1;
            audioSource.clip = GameDatabase.Instance.GetAudioClip(rocketSoundPath);

            UpdateVolume();
            BDArmorySetup.OnVolumeChange += UpdateVolume;
        }

        void UpdateVolume()
        {
            if (audioSource)
            {
                audioSource.volume = BDArmorySettings.BDARMORY_WEAPONS_VOLUME;
            }
        }
        void OnGUI()
        {
            if (((HighLogic.LoadedSceneIsFlight && BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS) || HighLogic.LoadedSceneIsFlight && !BDArmorySetup.GAME_UI_ENABLED && !MapView.MapIsEnabled && BDTISettings.TEAMICONS && BDTISettings.PERSISTANT) && BDTISettings.MISSILES)
            {
                if (distanceFromStart > 100)
                {
                    BDGUIUtils.DrawTextureOnWorldPos(transform.position, BDTISetup.Instance.TextureIconRocket, new Vector2(20, 20), 0);
                }
            }
        }
    }
}
