﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.UI;

namespace BDArmory.Competition
{

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class BDAScoreService : MonoBehaviour
    {
        public static BDAScoreService Instance;

        private HashSet<string> activePlayers = new HashSet<string>();
        public Dictionary<string, Dictionary<string, double>> timeOfLastHitOnTarget = new Dictionary<string, Dictionary<string, double>>();
        public Dictionary<string, Dictionary<string, int>> hitsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> hitsOut = new Dictionary<string, int>();
        public Dictionary<string, int> hitsIn = new Dictionary<string, int>();
        public Dictionary<string, double> damageOut = new Dictionary<string, double>();
        public Dictionary<string, double> damageIn = new Dictionary<string, double>();
        public Dictionary<string, Dictionary<string, int>> killsOnTarget = new Dictionary<string, Dictionary<string, int>>();
        public Dictionary<string, int> assists = new Dictionary<string, int>();
        public Dictionary<string, int> deaths = new Dictionary<string, int>();
        public Dictionary<string, double> HPremaining = new Dictionary<string, double>();
        public Dictionary<string, string> longestHitWeapon = new Dictionary<string, string>();
        public Dictionary<string, double> longestHitDistance = new Dictionary<string, double>();
        public Dictionary<string, int> rammedPartsOut = new Dictionary<string, int>();
        public Dictionary<string, int> rammedPartsIn = new Dictionary<string, int>();
        public Dictionary<string, int> missileStrikesOut = new Dictionary<string, int>();
        public Dictionary<string, int> missileStrikesIn = new Dictionary<string, int>();
        public Dictionary<string, int> missilePartsOut = new Dictionary<string, int>();
        public Dictionary<string, int> missilePartsIn = new Dictionary<string, int>();
        public Dictionary<string, double> missileDamageOut = new Dictionary<string, double>();
        public Dictionary<string, double> missileDamageIn = new Dictionary<string, double>();
        public Dictionary<string, int> rocketStrikesOut = new Dictionary<string, int>();
        public Dictionary<string, int> rocketStrikesIn = new Dictionary<string, int>();
        public Dictionary<string, int> rocketPartsOut = new Dictionary<string, int>();
        public Dictionary<string, int> rocketPartsIn = new Dictionary<string, int>();
        public Dictionary<string, double> rocketDamageOut = new Dictionary<string, double>();
        public Dictionary<string, double> rocketDamageIn = new Dictionary<string, double>();

        public enum StatusType
        {
            [Description("Offline")]
            Offline,
            [Description("Fetching Competition")]
            FetchingCompetition,
            [Description("Fetching Players")]
            FetchingPlayers,
            [Description("Waiting for Players")]
            PendingPlayers,
            [Description("Selecting a Heat")]
            FindingNextHeat,
            [Description("Fetching Heat")]
            FetchingHeat,
            [Description("Fetching Vessels")]
            FetchingVessels,
            [Description("Downloading Craft Files")]
            DownloadingCraftFiles,
            [Description("Starting Heat")]
            StartingHeat,
            [Description("Spawning Vessels")]
            SpawningVessels,
            [Description("Running Heat")]
            RunningHeat,
            [Description("Removing Vessels")]
            RemovingVessels,
            [Description("Stopping Heat")]
            StoppingHeat,
            [Description("Reporting Results")]
            ReportingResults,
            [Description("No Pending Heats")]
            StalledNoPendingHeats,
            [Description("Completed")]
            Completed,
            [Description("Cancelled")]
            Cancelled,
            [Description("Waiting")]
            Waiting,
            [Description("Stopped")]
            Stopped,
            [Description("Invalid")]
            Invalid
        }

        private bool pendingSync = false;
        private bool competitionStarted = false;
        public StatusType status = StatusType.Offline;

        private Coroutine syncCoroutine;

        //        protected CompetitionModel competition = null;

        //        protected HeatModel activeHeat = null;


        public BDAScoreClient client;

        public string vesselPath;

        void Awake()
        {
            if (Instance)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        void Update()
        {
            if (pendingSync && !Core.BDArmorySettings.REMOTE_LOGGING_ENABLED)
            {
                Debug.Log("[BDArmory.BDAScoreService] Cancel due to disable");
                pendingSync = false;
                if (syncCoroutine != null)
                    StopCoroutine(syncCoroutine);
                return;
            }
        }

        public void Configure(string vesselPath, string hash)
        {
            this.vesselPath = vesselPath;
            this.client = new BDAScoreClient(this, vesselPath, hash);
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
            syncCoroutine = StartCoroutine(SynchronizeWithService(hash));
            RemoteOrchestrationWindow.Instance.ShowWindow();
        }

        public void Cancel()
        {
            if (syncCoroutine != null)
                StopCoroutine(syncCoroutine);
            BDACompetitionMode.Instance.StopCompetition();
            pendingSync = false;
            status = status == StatusType.Waiting ? StatusType.Stopped : StatusType.Cancelled;
            if (status == StatusType.Cancelled)
            {
                Debug.Log("[BDArmory.BDAScoreService]: Cancelling the heat");
                // FIXME What else needs to be done to cancel a heat?
            }
            else
            {
                Debug.Log("[BDArmory.BDAScoreService]: Stopping score service.");
            }
        }

        public IEnumerator SynchronizeWithService(string hash)
        {
            if (pendingSync)
            {
                Debug.Log("[BDArmory.BDAScoreService] Sync in progress");
                yield break;
            }
            pendingSync = true;

            Debug.Log(string.Format("[BDArmory.BDAScoreService] Sync started {0}", hash));

            status = StatusType.FetchingCompetition;
            // first, get competition metadata
            yield return client.GetCompetition(hash);

            status = StatusType.FetchingPlayers;
            // next, get player metadata
            yield return client.GetPlayers(hash);

            // abort if we didn't receive a valid competition
            if (client.competition == null)
            {
                status = StatusType.Invalid;
                pendingSync = false;
                yield break;
            }

            switch (client.competition.status)
            {
                case 0:
                    status = StatusType.PendingPlayers;
                    // waiting for players; nothing to do
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Waiting for players {0}", hash));
                    break;
                case 1:
                    status = StatusType.FindingNextHeat;
                    // heats generated; find next heat
                    yield return FindNextHeat(hash);
                    break;
                case 2:
                    status = StatusType.StalledNoPendingHeats;
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Competition status 2 {0}", hash));
                    break;
            }

            pendingSync = false;
            Debug.Log(string.Format("[BDArmory.BDAScoreService] Sync completed {0}", hash));
        }

        private IEnumerator FindNextHeat(string hash)
        {
            Debug.Log(string.Format("[BDArmory.BDAScoreService] Find next heat for {0}", hash));

            status = StatusType.FetchingHeat;
            // fetch heat metadata
            yield return client.GetHeats(hash);

            // find an unstarted heat
            HeatModel model = client.heats.Values.FirstOrDefault(e => e.Available());
            if (model == null)
            {
                status = StatusType.StalledNoPendingHeats;
                Debug.Log(string.Format("[BDArmory.BDAScoreService] No inactive heat found {0}", hash));
                yield return RetryFind(hash);
            }
            else
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] Found heat {1} in {0}", hash, model.order));
                yield return FetchAndExecuteHeat(hash, model);
            }
        }

        public double retryFindStartedAt = -1;
        private IEnumerator RetryFind(string hash)
        {
            retryFindStartedAt = Planetarium.GetUniversalTime();
            yield return new WaitWhile(() => Planetarium.GetUniversalTime() - retryFindStartedAt < BDArmorySettings.REMOTE_INTERHEAT_DELAY);
            if (status != StatusType.Cancelled)
            {
                status = StatusType.FindingNextHeat;
                yield return FindNextHeat(hash);
            }
        }

        private IEnumerator FetchAndExecuteHeat(string hash, HeatModel model)
        {
            status = StatusType.FetchingVessels;
            // fetch vessel metadata for heat
            yield return client.GetVessels(hash, model);

            status = StatusType.DownloadingCraftFiles;
            // fetch craft files for vessels
            yield return client.GetCraftFiles(hash, model);

            status = StatusType.StartingHeat;
            // notify web service to start heat
            yield return client.StartHeat(hash, model);

            // execute heat
            int attempts = 0;
            competitionStarted = false;
            while (!competitionStarted && attempts++ < 3) // 3 attempts is plenty
            {
                yield return ExecuteHeat(hash, model);
                if (!competitionStarted)
                    switch (Control.VesselSpawner.Instance.spawnFailureReason)
                    {
                        case Control.VesselSpawner.SpawnFailureReason.None: // Successful spawning, but competition failed to start for some reason.
                            BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + BDACompetitionMode.Instance.competitionStartFailureReason + ", trying again.");
                            break;
                        case Control.VesselSpawner.SpawnFailureReason.VesselLostParts: // Recoverable spawning failure.
                        case Control.VesselSpawner.SpawnFailureReason.TimedOut: // Recoverable spawning failure.
                            BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + Control.VesselSpawner.Instance.spawnFailureReason + ", trying again.");
                            break;
                        default: // Spawning is unrecoverable.
                            BDACompetitionMode.Instance.competitionStatus.Add("Failed to start heat due to " + Control.VesselSpawner.Instance.spawnFailureReason + ", aborting.");
                            attempts = 3;
                            break;
                    }
            }

            status = StatusType.ReportingResults;
            // report scores
            yield return SendScores(hash, model);

            status = StatusType.StoppingHeat;
            // notify web service to stop heat
            yield return client.StopHeat(hash, model);

            status = StatusType.Waiting;
            yield return RetryFind(hash);
        }

        private IEnumerator ExecuteHeat(string hash, HeatModel model)
        {
            Debug.Log(string.Format("[BDArmory.BDAScoreService] Running heat {0}/{1}", hash, model.order));
            Control.VesselSpawner spawner = Control.VesselSpawner.Instance;

            // orchestrate the match
            activePlayers.Clear();
            assists.Clear();
            damageIn.Clear();
            damageOut.Clear();
            deaths.Clear();
            HPremaining.Clear();
            hitsIn.Clear();
            hitsOnTarget.Clear();
            hitsOut.Clear();
            killsOnTarget.Clear();
            longestHitDistance.Clear();
            longestHitWeapon.Clear();
            rocketDamageIn.Clear();
            rocketDamageOut.Clear();
            rocketPartsIn.Clear();
            rocketPartsOut.Clear();
            missileDamageIn.Clear();
            missileDamageOut.Clear();
            missilePartsIn.Clear();
            missilePartsOut.Clear();
            rammedPartsIn.Clear();
            rammedPartsOut.Clear();

            status = StatusType.SpawningVessels;
            var spawnConfig = new VesselSpawner.SpawnConfig(BDArmorySettings.VESSEL_SPAWN_WORLDINDEX, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, BDArmorySettings.VESSEL_SPAWN_ALTITUDE, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true, true, 0, null, null, hash);
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        spawnConfig = new VesselSpawner.SpawnConfig(BDArmorySettings.VESSEL_SPAWN_WORLDINDEX, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, 10f, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true, true, 0, null, null, hash);
                        break;
                    case 50: // FIXME temporary index, to be assigned later
                        spawnConfig = new VesselSpawner.SpawnConfig(BDArmorySettings.VESSEL_SPAWN_WORLDINDEX, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.x, BDArmorySettings.VESSEL_SPAWN_GEOCOORDS.y, FlightGlobals.currentMainBody.atmosphere ? (FlightGlobals.currentMainBody.atmosphereDepth + (FlightGlobals.currentMainBody.atmosphereDepth / 10)) : 50000, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE ? BDArmorySettings.VESSEL_SPAWN_DISTANCE : BDArmorySettings.VESSEL_SPAWN_DISTANCE_FACTOR, BDArmorySettings.VESSEL_SPAWN_DISTANCE_TOGGLE, BDArmorySettings.VESSEL_SPAWN_EASE_IN_SPEED, true, true, 0, null, null, hash);
                        break;
                }
            }
            spawner.SpawnAllVesselsOnce(spawnConfig);
            while (spawner.vesselsSpawning)
                yield return new WaitForFixedUpdate();
            if (!spawner.vesselSpawnSuccess)
            {
                Debug.Log("[BDArmory.BDAScoreService] Vessel spawning failed.");
                yield break;
            }
            yield return new WaitForFixedUpdate();

            status = StatusType.RunningHeat;
            // NOTE: runs in separate coroutine
            if (BDArmorySettings.RUNWAY_PROJECT)
            {
                switch (BDArmorySettings.RUNWAY_PROJECT_ROUND)
                {
                    case 33:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 44:
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    case 50: // FIXME temporary index, to be assigned later
                        BDACompetitionMode.Instance.StartRapidDeployment(0);
                        break;
                    default:
                        BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
                        break;
                }
            }
            else
                BDACompetitionMode.Instance.StartCompetitionMode(BDArmorySettings.COMPETITION_DISTANCE);
            yield return new WaitForFixedUpdate(); // Give the competition start a frame to get going.

            // start timer coroutine for the duration specified in settings UI
            var duration = Core.BDArmorySettings.COMPETITION_DURATION * 60d;
            var message = $"Starting {(duration > 0 ? $"a {duration.ToString("F0")}s" : "an unlimited")} duration competition for stage {model.stage} heat {model.order}.";
            Debug.Log("[BDArmory.BDAScoreService]: " + message);
            BDACompetitionMode.Instance.competitionStatus.Add(message);
            while (BDACompetitionMode.Instance.competitionStarting || BDACompetitionMode.Instance.sequencedCompetitionStarting)
                yield return new WaitForFixedUpdate(); // Wait for the competition to actually start.
            if (!BDACompetitionMode.Instance.competitionIsActive)
            {
                message = "Competition failed to start for heat " + hash + ".";
                BDACompetitionMode.Instance.competitionStatus.Add(message);
                Debug.Log("[BDArmory.BDAScoreService]: " + message);
                yield break;
            }
            competitionStarted = true;
            while (BDACompetitionMode.Instance.competitionIsActive) // Wait for the competition to finish (limited duration and log dumping is handled directly by the competition now).
                yield return new WaitForSeconds(1);
        }

        private IEnumerator SendScores(string hash, HeatModel heat)
        {
            var records = BuildRecords(hash, heat);
            yield return client.PostRecords(hash, heat.id, records.ToList());
        }

        private List<RecordModel> BuildRecords(string hash, HeatModel heat)
        {
            List<RecordModel> results = new List<RecordModel>();
            var playerNames = activePlayers;
            Debug.Log(string.Format("[BDArmory.BDAScoreService] Building records for {0} players", playerNames.Count));
            foreach (string playerName in playerNames)
            {
                if (!client.playerVessels.ContainsKey(playerName))
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Unmatched player {0}", playerName));
                    Debug.Log("DEBUG players were " + string.Join(", ", client.players.Values));
                    continue;
                }

                var playerNamePart = client.playerVessels[playerName].Item1;
                PlayerModel player = client.players.Values.FirstOrDefault(e => e.name == playerNamePart);
                if (player == null)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Unmatched player {0}", playerNamePart));
                    Debug.Log("DEBUG players were " + string.Join(", ", client.players.Values));
                    continue;
                }

                var vesselNamePart = client.playerVessels[playerName].Item2;
                VesselModel vessel = client.vessels.Values.FirstOrDefault(e => e.player_id == player.id && e.name == vesselNamePart);
                if (vessel == null)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Unmatched vessel for playerId {0}", player.id));
                    Debug.Log("DEBUG vessels were " + string.Join(", ", client.vessels.Values.Select(p => p.id)));
                    continue;
                }

                RecordModel record = new RecordModel();
                record.vessel_id = vessel.id;
                record.competition_id = int.Parse(hash);
                record.heat_id = heat.id;
                record.hits_out = ComputeTotalHitsOut(playerName);
                record.hits_in = ComputeTotalHitsIn(playerName);
                record.dmg_out = ComputeTotalDamageOut(playerName);
                record.dmg_in = ComputeTotalDamageIn(playerName);
                record.ram_parts_out = ComputeTotalRammedPartsOut(playerName);
                record.ram_parts_in = ComputeTotalRammedPartsIn(playerName);
                record.mis_strikes_out = ComputeTotalMissileStrikesOut(playerName);
                record.mis_strikes_in = ComputeTotalMissileStrikesIn(playerName);
                record.mis_parts_out = ComputeTotalMissilePartsOut(playerName);
                record.mis_parts_in = ComputeTotalMissilePartsIn(playerName);
                record.mis_dmg_out = ComputeTotalMissileDamageOut(playerName);
                record.mis_dmg_in = ComputeTotalMissileDamageIn(playerName);
                record.roc_strikes_out = ComputeTotalRocketStrikesOut(playerName);
                record.roc_strikes_in = ComputeTotalRocketStrikesIn(playerName);
                record.roc_parts_out = ComputeTotalRocketPartsOut(playerName);
                record.roc_parts_in = ComputeTotalRocketPartsIn(playerName);
                record.roc_dmg_out = ComputeTotalRocketDamageOut(playerName);
                record.roc_dmg_in = ComputeTotalRocketDamageIn(playerName);
                record.wins = ComputeWins(playerName);
                record.kills = ComputeTotalKills(playerName);
                record.deaths = ComputeTotalDeaths(playerName);
                record.HPremaining = ComputeAverageHPremaining(playerName);
                record.assists = ComputeTotalAssists(playerName);
                record.death_order = ComputeDeathOrder(playerName);
                record.death_time = ComputeDeathTime(playerName);
                if (longestHitDistance.ContainsKey(playerName) && longestHitWeapon.ContainsKey(playerName))
                {
                    record.distance = (float)longestHitDistance[playerName];
                    record.weapon = longestHitWeapon[playerName];
                }
                results.Add(record);
            }
            Debug.Log(string.Format("[BDArmory.BDAScoreService] Built records for {0} players", results.Count));
            return results;
        }

        private int ComputeTotalHitsOut(string playerName)
        {
            int result = 0;
            if (hitsOut.ContainsKey(playerName))
            {
                result = hitsOut[playerName];
            }
            return result;
        }

        private int ComputeTotalHitsIn(string playerName)
        {
            int result = 0;
            if (hitsIn.ContainsKey(playerName))
            {
                result = hitsIn[playerName];
            }
            return result;
        }

        private double ComputeTotalDamageOut(string playerName)
        {
            double result = 0;
            if (damageOut.ContainsKey(playerName))
            {
                result = damageOut[playerName];
            }
            return result;
        }

        private double ComputeTotalDamageIn(string playerName)
        {
            double result = 0;
            if (damageIn.ContainsKey(playerName))
            {
                result = damageIn[playerName];
            }
            return result;
        }

        private int ComputeTotalRammedPartsOut(string playerName)
        {
            if (rammedPartsOut.ContainsKey(playerName))
                return rammedPartsOut[playerName];
            return 0;
        }

        private int ComputeTotalRammedPartsIn(string playerName)
        {
            if (rammedPartsIn.ContainsKey(playerName))
                return rammedPartsIn[playerName];
            return 0;
        }

        private int ComputeTotalMissileStrikesOut(string playerName)
        {
            if (missileStrikesOut.ContainsKey(playerName))
                return missileStrikesOut[playerName];
            return 0;
        }

        private int ComputeTotalMissileStrikesIn(string playerName)
        {
            if (missileStrikesIn.ContainsKey(playerName))
                return missileStrikesIn[playerName];
            return 0;
        }

        private int ComputeTotalMissilePartsOut(string playerName)
        {
            if (missilePartsOut.ContainsKey(playerName))
                return missilePartsOut[playerName];
            return 0;
        }

        private int ComputeTotalMissilePartsIn(string playerName)
        {
            if (missilePartsIn.ContainsKey(playerName))
                return missilePartsIn[playerName];
            return 0;
        }

        private double ComputeTotalMissileDamageOut(string playerName)
        {
            if (missileDamageOut.ContainsKey(playerName))
                return missileDamageOut[playerName];
            return 0;
        }

        private double ComputeTotalMissileDamageIn(string playerName)
        {
            if (missileDamageIn.ContainsKey(playerName))
                return missileDamageIn[playerName];
            return 0;
        }

        private int ComputeTotalRocketStrikesOut(string playerName)
        {
            if (rocketStrikesOut.ContainsKey(playerName))
                return rocketStrikesOut[playerName];
            return 0;
        }

        private int ComputeTotalRocketStrikesIn(string playerName)
        {
            if (rocketStrikesIn.ContainsKey(playerName))
                return rocketStrikesIn[playerName];
            return 0;
        }

        private int ComputeTotalRocketPartsOut(string playerName)
        {
            if (rocketPartsOut.ContainsKey(playerName))
                return rocketPartsOut[playerName];
            return 0;
        }

        private int ComputeTotalRocketPartsIn(string playerName)
        {
            if (rocketPartsIn.ContainsKey(playerName))
                return rocketPartsIn[playerName];
            return 0;
        }

        private double ComputeTotalRocketDamageOut(string playerName)
        {
            if (rocketDamageOut.ContainsKey(playerName))
                return rocketDamageOut[playerName];
            return 0;
        }

        private double ComputeTotalRocketDamageIn(string playerName)
        {
            if (rocketDamageIn.ContainsKey(playerName))
                return rocketDamageIn[playerName];
            return 0;
        }

        private int ComputeTotalKills(string playerName)
        {
            int result = 0;
            if (killsOnTarget.ContainsKey(playerName))
            {
                result = killsOnTarget[playerName].Values.Sum();
            }
            return result;
        }

        private int ComputeTotalDeaths(string playerName)
        {
            int result = 0;
            if (deaths.ContainsKey(playerName))
            {
                result = deaths[playerName];
            }
            return result;
        }

        private double ComputeAverageHPremaining(string playerName)
        {
            double result = 0;
            var HPLeft = BDACompetitionMode.Instance.Scores;
            if (HPLeft.Players.Contains(playerName))
            {
                result = HPLeft.ScoreData[playerName].remainingHP;
            }
            return result;
        }

        private int ComputeTotalAssists(string playerName)
        {
            int result = 0;
            if (assists.ContainsKey(playerName))
            {
                result = assists[playerName];
            }
            return result;
        }

        private int ComputeWins(string playerName)
        {
            var stillAlive = !deaths.ContainsKey(playerName);
            var livingCount = activePlayers.Count - deaths.Count;
            return (stillAlive && livingCount == 1) ? 1 : 0;
        }

        private float ComputeDeathOrder(string playerName)
        {
            var scoreData = BDACompetitionMode.Instance.Scores.ScoreData;
            if (scoreData.ContainsKey(playerName) && scoreData[playerName].aliveState != AliveState.Alive)
            {
                return (float)scoreData[playerName].deathOrder / (float)activePlayers.Count;
            }
            else
            {
                return 1.0f;
            }
        }

        private float ComputeDeathTime(string playerName)
        {
            var scoreData = BDACompetitionMode.Instance.Scores.ScoreData;
            if (scoreData.ContainsKey(playerName) && scoreData[playerName].aliveState != AliveState.Alive)
            {
                return (float)scoreData[playerName].deathTime;
            }
            else
            {
                return BDArmorySettings.COMPETITION_DURATION * 60.0f;
            }
        }

        public void TrackDamage(string attacker, string target, double damage)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackDamage by {0} on {1} for {2}hp", target, attacker, damage));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (damageOut.ContainsKey(attacker))
            {
                damageOut[attacker] += damage;
            }
            else
            {
                damageOut.Add(attacker, damage);
            }
            if (damageIn.ContainsKey(target))
            {
                damageIn[target] += damage;
            }
            else
            {
                damageIn.Add(target, damage);
            }
        }

        public void TrackMissileStrike(string attacker, string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackMissileStrike by {0} on {1}", target, attacker));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (missileStrikesOut.ContainsKey(attacker))
            {
                missileStrikesOut[attacker]++;
            }
            else
            {
                missileStrikesOut.Add(attacker, 1);
            }
            if (missileStrikesIn.ContainsKey(target))
            {
                missileStrikesIn[target]++;
            }
            else
            {
                missileStrikesIn.Add(target, 1);
            }
        }

        public void TrackMissileDamage(string attacker, string target, double damage)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackMissileDamage by {0} on {1} for {2}hp", target, attacker, damage));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (missileDamageOut.ContainsKey(attacker))
            {
                missileDamageOut[attacker] += damage;
            }
            else
            {
                missileDamageOut.Add(attacker, damage);
            }
            if (missileDamageIn.ContainsKey(target))
            {
                missileDamageIn[target] += damage;
            }
            else
            {
                missileDamageIn.Add(target, damage);
            }
        }

        public void TrackMissileParts(string attacker, string target, int count)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackMissileParts by {0} on {1} for {2}parts", target, attacker, count));

            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (missilePartsOut.ContainsKey(attacker))
                missilePartsOut[attacker] += count;
            else
                missilePartsOut.Add(attacker, count);
            if (missilePartsIn.ContainsKey(target))
                missilePartsIn[target] += count;
            else
                missilePartsIn.Add(target, count);

            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                    timeOfLastHitOnTarget[attacker][target] = now;
                else
                    timeOfLastHitOnTarget[attacker].Add(target, now);
            }
            else
            {
                timeOfLastHitOnTarget.Add(attacker, new Dictionary<string, double> { { target, now } });
            }
        }

        public void TrackRocketStrike(string attacker, string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackRocketStrike by {0} on {1}", target, attacker));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (rocketStrikesOut.ContainsKey(attacker))
            {
                rocketStrikesOut[attacker]++;
            }
            else
            {
                rocketStrikesOut.Add(attacker, 1);
            }
            if (rocketStrikesIn.ContainsKey(target))
            {
                rocketStrikesIn[target]++;
            }
            else
            {
                rocketStrikesIn.Add(target, 1);
            }
        }

        public void TrackRocketDamage(string attacker, string target, double damage)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackRocketDamage by {0} on {1} for {2}hp", target, attacker, damage));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (rocketDamageOut.ContainsKey(attacker))
            {
                rocketDamageOut[attacker] += damage;
            }
            else
            {
                rocketDamageOut.Add(attacker, damage);
            }
            if (rocketDamageIn.ContainsKey(target))
            {
                rocketDamageIn[target] += damage;
            }
            else
            {
                rocketDamageIn.Add(target, damage);
            }
        }

        public void TrackRocketParts(string attacker, string target, int count)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackRocketParts by {0} on {1} for {2}parts", target, attacker, count));

            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (rocketPartsOut.ContainsKey(attacker))
                rocketPartsOut[attacker] += count;
            else
                rocketPartsOut.Add(attacker, count);
            if (rocketPartsIn.ContainsKey(target))
                rocketPartsIn[target] += count;
            else
                rocketPartsIn.Add(target, count);

            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                    timeOfLastHitOnTarget[attacker][target] = now;
                else
                    timeOfLastHitOnTarget[attacker].Add(target, now);
            }
            else
            {
                timeOfLastHitOnTarget.Add(attacker, new Dictionary<string, double> { { target, now } });
            }
        }

        public void TrackRammedParts(string attacker, string target, int count)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackRammedParts by {0} on {1} for {2}parts", target, attacker, count));

            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (rammedPartsOut.ContainsKey(attacker))
                rammedPartsOut[attacker] += count;
            else
                rammedPartsOut.Add(attacker, count);
            if (rammedPartsIn.ContainsKey(target))
                rammedPartsIn[target] += count;
            else
                rammedPartsIn.Add(target, count);

            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                    timeOfLastHitOnTarget[attacker][target] = now;
                else
                    timeOfLastHitOnTarget[attacker].Add(target, now);
            }
            else
            {
                timeOfLastHitOnTarget.Add(attacker, new Dictionary<string, double> { { target, now } });
            }
        }

        public void TrackHit(string attacker, string target, string weaponName, double hitDistance)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackHit by {0} on {1} with {2} at {3}m", target, attacker, weaponName, hitDistance));
            }
            double now = Planetarium.GetUniversalTime();
            activePlayers.Add(attacker);
            activePlayers.Add(target);
            if (hitsOnTarget.ContainsKey(attacker))
            {
                if (hitsOnTarget[attacker].ContainsKey(target))
                {
                    ++hitsOnTarget[attacker][target];
                }
                else
                {
                    hitsOnTarget[attacker].Add(target, 1);
                }
            }
            else
            {
                var newHits = new Dictionary<string, int>();
                newHits.Add(target, 1);
                hitsOnTarget.Add(attacker, newHits);
            }
            if (hitsOut.ContainsKey(attacker))
            {
                ++hitsOut[attacker];
            }
            else
            {
                hitsOut.Add(attacker, 1);
            }
            if (hitsIn.ContainsKey(target))
            {
                ++hitsIn[target];
            }
            else
            {
                hitsIn.Add(target, 1);
            }
            if (!longestHitDistance.ContainsKey(attacker) || hitDistance > longestHitDistance[attacker])
            {
                Debug.Log(string.Format("[BDACompetitionMode] Tracked longest hit for {0} with {1} at {2}m", attacker, weaponName, hitDistance));
                if (longestHitDistance.ContainsKey(attacker))
                {
                    longestHitWeapon[attacker] = weaponName;
                    longestHitDistance[attacker] = hitDistance;
                }
                else
                {
                    longestHitWeapon.Add(attacker, weaponName);
                    longestHitDistance.Add(attacker, hitDistance);
                }
            }
            if (timeOfLastHitOnTarget.ContainsKey(attacker))
            {
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target))
                {
                    timeOfLastHitOnTarget[attacker][target] = now;
                }
                else
                {
                    timeOfLastHitOnTarget[attacker].Add(target, now);
                }
            }
            else
            {
                var newTimeOfLast = new Dictionary<string, double>();
                newTimeOfLast.Add(target, now);
                timeOfLastHitOnTarget.Add(attacker, newTimeOfLast);
            }
        }

        public void ComputeAssists(string target, string killer = "", double timeLimit = 30)
        {
            var now = Planetarium.GetUniversalTime();
            var thresholdTime = now - timeLimit; // anyone who hit this target within the last 30sec

            foreach (var attacker in timeOfLastHitOnTarget.Keys)
            {
                if (attacker == killer) continue; // Don't award assists to the killer.
                if (timeOfLastHitOnTarget[attacker].ContainsKey(target) && timeOfLastHitOnTarget[attacker][target] > thresholdTime)
                {
                    if (assists.ContainsKey(attacker))
                    {
                        ++assists[attacker];
                    }
                    else
                    {
                        assists.Add(attacker, 1);
                    }
                }
            }
        }

        /**
         * Tracks a death.
         */
        public void TrackDeath(string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackDeath for {0}", target));
            }
            activePlayers.Add(target);
            IncrementDeath(target);
        }

        private void IncrementDeath(string target)
        {
            if (deaths.ContainsKey(target))
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] IncrementDeaths for {0}", target));
                }
                ++deaths[target];
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] FirstDeath for {0}", target));
                }
                deaths.Add(target, 1);
            }
        }

        /**
         * Tracks a clean kill, when an attacker decisively kills the target.
         */
        public void TrackKill(string attacker, string target)
        {
            if (BDArmorySettings.DRAW_DEBUG_LABELS)
            {
                Debug.Log(string.Format("[BDArmory.BDAScoreService] TrackKill {0} by {1}", target, attacker));
            }
            activePlayers.Add(attacker);
            activePlayers.Add(target);

            IncrementKill(attacker, target);
            // ComputeAssists(target, attacker, 30);
        }

        private void IncrementKill(string attacker, string target)
        {
            // increment kill counter
            if (killsOnTarget.ContainsKey(attacker))
            {
                if (killsOnTarget[attacker].ContainsKey(target))
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    {
                        Debug.Log(string.Format("[BDArmory.BDAScoreService] IncrementKills for {0} on {1}", attacker, target));
                    }
                    ++killsOnTarget[attacker][target];
                }
                else
                {
                    if (BDArmorySettings.DRAW_DEBUG_LABELS)
                    {
                        Debug.Log(string.Format("[BDArmory.BDAScoreService] Kill for {0} on {1}", attacker, target));
                    }
                    killsOnTarget[attacker].Add(target, 1);
                }
            }
            else
            {
                if (BDArmorySettings.DRAW_DEBUG_LABELS)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] FirstKill for {0} on {1}", attacker, target));
                }
                var newKills = new Dictionary<string, int>();
                newKills.Add(target, 1);
                killsOnTarget.Add(attacker, newKills);
            }
        }

        // Register survivors in case they didn't really do anything and didn't get registered until now.
        public void TrackSurvivors(List<string> survivors)
        {
            foreach (var survivor in survivors)
                activePlayers.Add(survivor);
        }

        public string Status()
        {
            return status.ToString();
        }

        public class JsonListHelper<T>
        {
            [Serializable]
            private class Wrapper<S>
            {
                public S[] items;
            }
            public List<T> FromJSON(string json)
            {
                if (json == null)
                {
                    return new List<T>();
                }
                //string wrappedJson = string.Format("{{\"items\":{0}}}", json);
                Wrapper<T> wrapper = new Wrapper<T>();
                wrapper.items = JsonUtility.FromJson<T[]>(json);
                if (wrapper == null || wrapper.items == null)
                {
                    Debug.Log(string.Format("[BDArmory.BDAScoreService] Failed to decode {0}", json));
                    return new List<T>();
                }
                else
                {
                    return new List<T>(wrapper.items);
                }
            }
        }
    }
}
