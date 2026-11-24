using System;
using System.Reflection;
using HarmonyLib;
using Il2CppIGG.Game.Module.RaceCar;
using Il2CppProtomsg;

namespace WestlandRacerMod
{
    /// <summary>
    /// Purely observational postfixes on the game's reply handlers.
    ///
    /// The mod does not change any game behaviour: it reads the server's
    /// <c>ErrorCode</c> after the game's own handler ran, and forwards it to the
    /// brain so the auto-player can back off or re-pick a car instead of
    /// hammering the server with requests that will never succeed.
    ///
    /// Patched by hand (like the other doomsday mods) so one missing method
    /// costs a single log line instead of the whole patch set.
    /// </summary>
    internal static class ReplyPatches
    {
        private const string HarmonyId = "com.alireza.westlandracermod";
        private static RacerBrain _brain;

        public static bool Applied { get; private set; }

        public static void Apply(RacerBrain brain)
        {
            _brain = brain;
            try
            {
                var harmony = new HarmonyLib.Harmony(HarmonyId);
                bool any = false;

                any |= TryPatch(harmony, "OnMsgGS2CLRaceCarMatchReply",
                    AccessTools.Method(typeof(RaceCarModule), "OnMsgGS2CLRaceCarMatchReply",
                        new[] { typeof(MsgGS2CLRaceCarMatchReply) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(AfterMatchReply),
                        new[] { typeof(MsgGS2CLRaceCarMatchReply) }));

                any |= TryPatch(harmony, "OnMsgGS2CLRaceCarSelectCarReply",
                    AccessTools.Method(typeof(RaceCarModule), "OnMsgGS2CLRaceCarSelectCarReply",
                        new[] { typeof(MsgGS2CLRaceCarSelectCarReply) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(AfterSelectCarReply),
                        new[] { typeof(MsgGS2CLRaceCarSelectCarReply) }));

                any |= TryPatch(harmony, "OnMsgGS2CLRaceCarUseCardReply",
                    AccessTools.Method(typeof(RaceCarModule), "OnMsgGS2CLRaceCarUseCardReply",
                        new[] { typeof(MsgGS2CLRaceCarUseCardReply) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(AfterUseCardReply),
                        new[] { typeof(MsgGS2CLRaceCarUseCardReply) }));

                any |= TryPatch(harmony, "OnMsgGS2CLRaceCarUsePropReply",
                    AccessTools.Method(typeof(RaceCarModule), "OnMsgGS2CLRaceCarUsePropReply",
                        new[] { typeof(MsgGS2CLRaceCarUsePropReply) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(AfterUsePropReply),
                        new[] { typeof(MsgGS2CLRaceCarUsePropReply) }));

                // ---- Outbound observers: log the exact payload of every request,
                // whichever side sent it (UI or this mod). Purely diagnostic.
                TryPatchPrefix(harmony, "API.SendRaceCarMatchRequest",
                    AccessTools.Method(typeof(RaceCarModule.API), "SendRaceCarMatchRequest",
                        new[] { typeof(uint) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(BeforeMatchRequest),
                        new[] { typeof(uint) }));

                TryPatchPrefix(harmony, "API.SendRaceCarSelectCarRequest",
                    AccessTools.Method(typeof(RaceCarModule.API), "SendRaceCarSelectCarRequest",
                        new[] { typeof(uint), typeof(uint) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(BeforeSelectCarRequest),
                        new[] { typeof(uint), typeof(uint) }));
                TryPatchPrefix(harmony, "API.SendRaceCarUseCardRequest",
                    AccessTools.Method(typeof(RaceCarModule.API), "SendRaceCarUseCardRequest",
                        new[] { typeof(uint), typeof(Il2CppSystem.Collections.Generic.List<uint>) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(BeforeUseCardRequest),
                        new[] { typeof(uint), typeof(Il2CppSystem.Collections.Generic.List<uint>) }));

                TryPatchPrefix(harmony, "API.SendRaceCarUsePropRequest",
                    AccessTools.Method(typeof(RaceCarModule.API), "SendRaceCarUsePropRequest",
                        new[] { typeof(uint), typeof(uint), typeof(uint) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(BeforeUsePropRequest),
                        new[] { typeof(uint), typeof(uint), typeof(uint) }));

                any |= TryPatch(harmony, "OnMsgGS2CLRaceCarSettleDataNotice",
                    AccessTools.Method(typeof(RaceCarModule), "OnMsgGS2CLRaceCarSettleDataNotice",
                        new[] { typeof(MsgGS2CLRaceCarSettleDataNotice) }),
                    AccessTools.Method(typeof(ReplyPatches), nameof(AfterSettleDataNotice),
                        new[] { typeof(MsgGS2CLRaceCarSettleDataNotice) }));

                Applied = any;
                Log.Info(Applied
                    ? "Reply observer patches applied."
                    : "No reply patches could be applied; auto-play will use fixed cooldowns only.");
            }
            catch (Exception ex)
            {
                Log.Warn("Reply patches failed: " + ex.Message);
                Applied = false;
            }
        }

        private static bool TryPatch(HarmonyLib.Harmony harmony, string label, MethodInfo original, MethodInfo postfix)
        {
            if (original == null)
            {
                Log.Info("patch skipped: " + label + " not found in this build");
                return false;
            }
            if (postfix == null)
            {
                Log.Info("patch skipped: observer not found for " + label);
                return false;
            }
            try
            {
                harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                Log.Info("patch applied: " + label);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("patch FAILED for " + label + ": " + ex.Message);
                return false;
            }
        }

        private static void TryPatchPrefix(HarmonyLib.Harmony harmony, string label, MethodInfo original, MethodInfo prefix)
        {
            if (original == null)
            {
                Log.Info("outbound patch skipped: " + label + " not found in this build");
                return;
            }
            if (prefix == null)
            {
                Log.Info("outbound patch skipped: observer not found for " + label);
                return;
            }
            try
            {
                harmony.Patch(original, prefix: new HarmonyMethod(prefix));
                Log.Info("outbound patch applied: " + label);
            }
            catch (Exception ex)
            {
                Log.Warn("outbound patch FAILED for " + label + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Outbound prefix bodies (observation only)
        // ------------------------------------------------------------------

        private static void BeforeMatchRequest(uint activityId)
        {
            try { Log.Info("[api-out] SendRaceCarMatchRequest(actId=" + activityId + ")"); } catch { }
        }

        private static void BeforeSelectCarRequest(uint activityId, uint car)
        {
            try { Log.Info("[api-out] SendRaceCarSelectCarRequest(actId=" + activityId + ", carId=" + car + ")"); } catch { }
        }

        private static void BeforeUseCardRequest(uint activityId, Il2CppSystem.Collections.Generic.List<uint> cards)
        {
            try
            {
                string ids = "?";
                if (cards != null)
                {
                    var idsList = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < cards.Count; i++) idsList.Add(cards[i].ToString());
                    ids = "[" + string.Join(",", idsList) + "] n=" + cards.Count;
                }
                Log.Info("[api-out] SendRaceCarUseCardRequest(actId=" + activityId + ", cards=" + ids + ")");
            }
            catch { }
        }

        private static void BeforeUsePropRequest(uint activityId, uint propItems, uint index)
        {
            try { Log.Info("[api-out] SendRaceCarUsePropRequest(actId=" + activityId + ", propId=" + propItems + ", index=" + index + ")"); } catch { }
        }

        // ------------------------------------------------------------------
        // Postfix bodies
        // ------------------------------------------------------------------

        private static void AfterMatchReply(MsgGS2CLRaceCarMatchReply msg)
        {
            try
            {
                if (msg == null || _brain == null) return;
                _brain.OnMatchReply(msg.ErrorCode);
            }
            catch { }
        }

        private static void AfterSelectCarReply(MsgGS2CLRaceCarSelectCarReply msg)
        {
            try
            {
                if (msg == null || _brain == null) return;
                _brain.OnSelectCarReply(msg.CarId, msg.ErrorCode);
            }
            catch { }
        }

        private static void AfterUseCardReply(MsgGS2CLRaceCarUseCardReply msg)
        {
            try
            {
                if (msg == null || _brain == null) return;
                int count = 0;
                try { count = msg.Cards != null ? msg.Cards.Count : 0; } catch { }
                _brain.OnUseCardReply(msg.ErrorCode, count);
            }
            catch { }
        }

        private static void AfterUsePropReply(MsgGS2CLRaceCarUsePropReply msg)
        {
            try
            {
                if (msg == null || _brain == null) return;
                _brain.OnUsePropReply(msg.Prop, msg.ErrorCode);
            }
            catch { }
        }

        private static void AfterSettleDataNotice(MsgGS2CLRaceCarSettleDataNotice msg)
        {
            try
            {
                if (msg == null || _brain == null) return;
                // Only count the notices for our own player.
                if (msg.PlayerId != 0 && msg.PlayerId != RaceApi.GetLocalPlayerId()) return;
                var data = msg.Data;
                if (data == null) return;
                _brain.OnSelfSettled(msg.RoundId, data.Rank, data.TotalScore, data.TotalRank);
            }
            catch { }
        }
    }
}
