// Shoutout to Toaster for ToastersRinkCompanion and ToastersCellyCam,
// I used them as a starting point for this mod and it helped a lot.
// https://github.com/ckhawks/ToastersRinkCompanion
// https://github.com/ckhawks/ToasterCellyCam

using System;
using HarmonyLib;
using UnityEngine;
using System.Collections;
using System.Linq;
using UnityEngine.Rendering;

namespace BetterReplays
{
  public class BetterReplaysPlugin : IPuckMod
  {
    public static readonly string MOD_NAME = "ypdubaisBetterReplays";
    public static readonly string MOD_VERSION = "1.0.0";

    static readonly Harmony harmony = new Harmony("ypdubai.betterreplays");
    private static Player goalScorer;
    private static Goal scoredGoal;

    private static BetterReplaysHandler betterReplayHandler;

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.Client_EnableReplayCamera))]
    public class ClientEnableReplayCameraPatch
    {
      [HarmonyPostfix]
      static void Postfix(ReplayManager __instance, BaseCamera ___replayCamera)
      {
        Log("Goal replay starting - checking configuration...");
        BetterReplaysConfig.LoadConfig();

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
        if (IsDedicatedServer())
        {
          LogError("Environment: dedicated server.");
          LogError("This is only meant to be used on clients!");
          return false;
        }
        else
        {
          Log("Bettering replays...");
          Log("Initializing configuration...");
          BetterReplaysConfig.LoadConfig();

          harmony.PatchAll();
          Log("List of patched methods:");
          LogAllPatchedMethods();
          Log("Replays bettered.");
          return true;
        }
      }
      catch (Exception e)
      {
        LogError($"Failed to enable: {e.Message}");
        return false;
      }
    }

    public bool OnDisable()
    {
      try
      {
        Log("Unbettering replays...");
        harmony.UnpatchSelf();
        Log("Replays unbettered.");
      }
      catch (Exception e)
      {
        LogError($"Failed to disable: {e.Message}");
        return false;
      }
      return true;
    }

    // Copied this from https://github.com/ckhawks/ToastersRinkCompanion/blob/main/src/Plugin.cs
    public static bool IsDedicatedServer()
    {
      return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    }

    // Copied this from https://github.com/ckhawks/ToastersRinkCompanion/blob/main/src/Plugin.cs
    public static void LogAllPatchedMethods()
    {
      var allPatchedMethods = harmony.GetPatchedMethods();
      var pluginId = harmony.Id;

      var mine = allPatchedMethods
          .Select(m => new { method = m, info = Harmony.GetPatchInfo(m) })
          .Where(x =>
              x.info.Prefixes.Any(p => p.owner == pluginId) ||
              x.info.Postfixes.Any(p => p.owner == pluginId) ||
              x.info.Transpilers.Any(p => p.owner == pluginId) ||
              x.info.Finalizers.Any(p => p.owner == pluginId)
          )
          .Select(x => x.method);

      foreach (var m in mine)
        Log($" - {m.DeclaringType.FullName}.{m.Name}");
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