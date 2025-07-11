using System;
using HarmonyLib;
using UnityEngine;
using System.Collections;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace BetterReplays
{
  public class BetterReplaysPlugin : IPuckMod
  {
    public static readonly string MOD_NAME = "ypdubaisBetterReplays";
    public static readonly string MOD_VERSION = "1.0.0";

    static readonly Harmony harmony = new Harmony("ypdubai.betterreplays");
    private static Player goalScorer;
    private static TMP_Text goalScorerUsernameText;
    private static TMP_Text goalScorerNumberText;
    private static Goal scoredGoal;

    private static BetterReplaysHandler betterReplayHandler;

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.Client_EnableReplayCamera))]
    public class ClientEnableReplayCameraPatch
    {
      [HarmonyPostfix]
      static void Postfix(ReplayManager __instance, BaseCamera ___replayCamera)
      {
        betterReplayHandler = __instance.ReplayRecorder.gameObject.AddComponent<BetterReplaysHandler>();
        betterReplayHandler.SetReplayCamera(___replayCamera);

        // Start coroutine to wait for replay player to spawn before setting goal scorer
        betterReplayHandler.StartCoroutine(WaitAndSetGoalScorer());
        betterReplayHandler.StartCoroutine(WaitForGoal());

        if (scoredGoal != null)
        {
          betterReplayHandler.SetGoal(scoredGoal);
        }
      }

      static IEnumerator WaitAndSetGoalScorer()
      {
        yield return new WaitForSeconds(0.1f); // Wait 0.1 seconds for replay player to spawn

        if (goalScorer != null && betterReplayHandler != null)
        {
          betterReplayHandler.SetGoalScorer(goalScorer);
        }
      }

      static IEnumerator WaitForGoal()
      {
        yield return new WaitForSeconds(7.0f);

        if (betterReplayHandler != null)
        {
          betterReplayHandler.SetGoalScored(true);
        }
      }
    }

    [HarmonyPatch(typeof(GoalTrigger))]
    [HarmonyPatch("OnTriggerEnter", MethodType.Normal)]
    public static class GoalTriggerOnTriggerEnterPatch
    {
      [HarmonyPostfix]
      public static void Postfix(Goal ___goal)
      {
        scoredGoal = ___goal;

        if (betterReplayHandler != null)
        {
          betterReplayHandler.SetGoal(___goal);
        }
      }
    }

    // Track the goal scoring player
    [HarmonyPatch(typeof(UIAnnouncement), nameof(UIAnnouncement.ShowBlueTeamScoreAnnouncement))]
    public static class UIAnnouncementShowBlueTeamScoreAnnouncement
    {
      [HarmonyPostfix]
      public static void Postfix(UIAnnouncement __instance, float time, Player goalPlayer, Player assistPlayer, Player secondAssistPlayer)
      {
        goalScorer = goalPlayer;
      }
    }

    [HarmonyPatch(typeof(UIAnnouncement), nameof(UIAnnouncement.ShowRedTeamScoreAnnouncement))]
    public static class UIAnnouncementShowRedTeamScoreAnnouncement
    {
      [HarmonyPostfix]
      public static void Postfix(UIAnnouncement __instance, float time, Player goalPlayer, Player assistPlayer, Player secondAssistPlayer)
      {
        goalScorer = goalPlayer;
      }
    }

    public bool OnEnable()
    {
      try
      {
        harmony.PatchAll();
      }
      catch (Exception e)
      {
        LogError($"Harmony patch failed: {e.Message}");
        return false;
      }

      return true;
    }

    public bool OnDisable()
    {
      try
      {
        harmony.UnpatchSelf();
      }
      catch (Exception e)
      {
        LogError($"Harmony unpatch failed: {e.Message}");
        return false;
      }
      return true;
    }

    public static void Log(string message)
    {
      Debug.Log($"[{MOD_NAME}] {message}");
    }

    public static void LogError(string message)
    {
      Debug.LogError($"[{MOD_NAME}] {message}");
    }

    public static void LogWarning(string message)
    {
      Debug.LogWarning($"[{MOD_NAME}] {message}");
    }
  }
}