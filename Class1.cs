using System;
using HarmonyLib;
using SingularityGroup.HotReload;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections;
using Object = UnityEngine.Object;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Linq;
using UnityEngine.InputSystem;

namespace BetterReplays
{
  public class BetterReplaysPlugin : IPuckMod
  {
    static readonly Harmony harmony = new Harmony("YPD.mod");
    private static Player goalScorer;
    private static Player replayPlayer;
    private static Goal scoredGoal;

    private static BetterReplayHandler betterReplayHandler;

    [HarmonyPatch(typeof(LevelManager), nameof(LevelManager.Client_EnableReplayCamera))]
    public class ClientEnableReplayCameraPatch
    {
      [HarmonyPostfix]
      static void Postfix(ReplayManager __instance, BaseCamera ___replayCamera)
      {
        betterReplayHandler = __instance.ReplayRecorder.gameObject.AddComponent<BetterReplayHandler>();
        float userFOV = SettingsManager.Instance.Fov;
        ___replayCamera.SetFieldOfView(userFOV + 45); // this value doesn't match the in game value, no clue why
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
        Debug.Log("Goal trigger entered on: " + ___goal.name);

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
        Debug.Log("Set goalScorer to " + goalPlayer.name);
      }
    }

    [HarmonyPatch(typeof(UIAnnouncement), nameof(UIAnnouncement.ShowRedTeamScoreAnnouncement))]
    public static class UIAnnouncementShowRedTeamScoreAnnouncement
    {
      [HarmonyPostfix]
      public static void Postfix(UIAnnouncement __instance, float time, Player goalPlayer, Player assistPlayer, Player secondAssistPlayer)
      {
        goalScorer = goalPlayer;
        Debug.Log("Set goalScorer to " + goalPlayer.name);
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
        Debug.LogError($"Harmony patch failed: {e.Message}");
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
        Debug.LogError($"Harmony unpatch failed: {e.Message}");
        return false;
      }
      return true;
    }
  }

  public class BetterReplayHandler : MonoBehaviour
  {
    private Player goalScorer;
    private BaseCamera camera;
    private bool goalScored = false;
    private Goal goal;
    private bool isFirstPerson = false; // Start in third person

    // Free look variables
    private bool isFreeLook = false;
    private Vector2 freeLookRotation = Vector2.zero;
    private Quaternion originalRotation;
    private Vector3 originalPosition;
    private float lookSensitivity;
    private bool isTransitioningToFreeLook = false;
    private float freeLookTransitionTime = 0f;

    public void Start()
    {
      lookSensitivity = SettingsManager.Instance.LookSensitivity;

      // Destroy this component after 10 seconds
      Destroy(this, 10.0f);
    }

    public void SetGoal(Goal goal)
    {
      this.goal = goal;
    }

    public void SetGoalScorer(Player goalScorer)
    {
      foreach (Player replayPlayerSearch in PlayerManager.Instance.GetReplayPlayers())
      {
        if (replayPlayerSearch.Username.Value.ToString() == goalScorer.Username.Value.ToString() &&
            replayPlayerSearch.Number.Value.ToString() == goalScorer.Number.Value.ToString())
        {
          this.goalScorer = replayPlayerSearch;
        }
      }

      if (this.goalScorer == null)
      {
        Debug.Log("Could not locate replay player for goal scorer!");
      }
      else
      {
        // Initialize camera position if camera is already set
        if (camera != null)
        {
          InitializeCameraPosition();
        }
      }
    }

    public void SetReplayCamera(BaseCamera camera)
    {
      this.camera = camera;
      Debug.Log("Inside BetterReplayHandler: Set camera");

      // Initialize camera position behind goal scorer if available
      if (goalScorer != null)
      {
        InitializeCameraPosition();
      }
    }

    private void InitializeCameraPosition()
    {
      if (goalScorer == null || camera == null) return;

      // Position camera behind the goal scorer initially
      Vector3 playerPosition = goalScorer.PlayerCamera.transform.position;
      Vector3 playerForward = goalScorer.PlayerCamera.transform.forward;

      // Place camera behind player (opposite to forward direction)
      Vector3 initialPosition = playerPosition - playerForward * 3.0f; // 3 units behind

      camera.transform.position = initialPosition;
      camera.transform.rotation = goalScorer.PlayerCamera.transform.rotation;

      Debug.Log("Initialized camera position behind goal scorer");
    }

    public void SetGoalScored(bool goalScored)
    {
      this.goalScored = goalScored;
    }

    public void Update()
    {
      if (goalScorer == null || camera == null || goal == null)
      {
        return;
      }

      // Check for C key press to toggle camera mode (only during replays)
      if (Keyboard.current.cKey.wasPressedThisFrame)
      {
        isFirstPerson = !isFirstPerson;
        Debug.Log("Camera mode switched to: " + (isFirstPerson ? "First Person" : "Third Person"));
      }

      // Handle right-click free look
      if (Mouse.current.rightButton.wasPressedThisFrame)
      {
        isFreeLook = true;
        isTransitioningToFreeLook = true;
        freeLookTransitionTime = 0f;
        originalRotation = camera.transform.rotation;
        originalPosition = camera.transform.position;

        // Initialize freeLookRotation from current camera rotation
        Vector3 currentEuler = camera.transform.rotation.eulerAngles;
        freeLookRotation.x = currentEuler.y; // Yaw
        freeLookRotation.y = currentEuler.x; // Pitch

        // Handle angle wrapping for pitch
        if (freeLookRotation.y > 180f)
          freeLookRotation.y -= 360f;

        Cursor.lockState = CursorLockMode.Locked;
      }

      if (Mouse.current.rightButton.wasReleasedThisFrame)
      {
        isFreeLook = false;
        isTransitioningToFreeLook = false;
        Cursor.lockState = CursorLockMode.None;
      }

      ReplayCamera rc = (ReplayCamera)camera;

      if (!isFirstPerson)
      {
        if (goalScored)
        {
          rc.SetTarget(goalScorer.PlayerCamera.transform);
        }
        else
        {
          rc.SetTarget(goal.transform);
        }
      }
      else
      {
        rc.SetTarget(null);
      }

      Vector3 targetPosition = goalScorer.PlayerCamera.transform.position;

      // For third person, adjust target position to be above and behind player
      if (!isFirstPerson)
      {
        Vector3 playerPosition = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;
        Vector3 playerForward = goalScorer.PlayerCamera.transform.forward;

        // Position camera above and behind the player
        Vector3 offset = -playerForward * 1.0f + Vector3.up * 0.5f;
        targetPosition = playerPosition + offset;
      }

      Quaternion targetRotation = goalScorer.PlayerCamera.transform.rotation;

      if (isFirstPerson)
      {
        HandleFirstPersonCamera(targetPosition, targetRotation);
      }
      else
      {
        HandleThirdPersonCamera(targetPosition, targetRotation);
      }
    }

    private void HandleFirstPersonCamera(Vector3 targetPosition, Quaternion targetRotation)
    {
      if (isFreeLook)
      {
        // Free look in first person - rotate camera based on mouse movement
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        freeLookRotation.x += mouseDelta.x * this.lookSensitivity;
        freeLookRotation.y -= mouseDelta.y * this.lookSensitivity;
        freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, -90f, 90f);

        Quaternion freeLookQuat = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
        camera.transform.SetPositionAndRotation(targetPosition, freeLookQuat);
      }
      else
      {
        // Normal first person - lerp back to player rotation
        float rotationLerpSpeed = 0.1f;
        Quaternion smoothedRotation = Quaternion.Lerp(camera.transform.rotation, targetRotation, rotationLerpSpeed);
        camera.transform.SetPositionAndRotation(targetPosition, smoothedRotation);
      }

      hideGoalScorer();
    }

    private void HandleThirdPersonCamera(Vector3 targetPosition, Quaternion targetRotation)
    {
      float lerpSpeed = 0.02f;
      Vector3 smoothedPosition;

      if (isFreeLook)
      {
        // Free look in third person - orbit around player
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        freeLookRotation.x += mouseDelta.x * lookSensitivity;
        freeLookRotation.y -= mouseDelta.y * lookSensitivity;
        freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, -60f, 80f);

        // Calculate orbital position around player
        Vector3 playerHead = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;
        float distance = 3.0f; // Orbital distance

        Quaternion orbitalRotation = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
        Vector3 orbitalPosition = playerHead + orbitalRotation * Vector3.back * distance;

        smoothedPosition = Vector3.Lerp(camera.transform.position, orbitalPosition, 0.05f);

        // Calculate target rotation (looking at player)
        Vector3 lookDirection = (playerHead - smoothedPosition).normalized;
        Quaternion targetLookRotation = Quaternion.LookRotation(lookDirection);

        // Handle transition to free look with smooth rotation
        if (isTransitioningToFreeLook)
        {
          freeLookTransitionTime += Time.deltaTime;
          float transitionDuration = 0.5f; // 0.5 seconds to transition
          float t = Mathf.Clamp01(freeLookTransitionTime / transitionDuration);

          // Lerp from original rotation to look-at-player rotation
          Quaternion smoothedRotation = Quaternion.Lerp(originalRotation, targetLookRotation, t);
          camera.transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);

          // End transition when complete
          if (t >= 1f)
          {
            isTransitioningToFreeLook = false;
          }
        }
        else
        {
          // Normal free look - snap to look at player
          camera.transform.SetPositionAndRotation(smoothedPosition, targetLookRotation);
        }
      }
      else
      {
        // Normal third person behavior
        smoothedPosition = Vector3.Lerp(camera.transform.position, targetPosition, lerpSpeed);

        // Enforce minimum distance in third person
        float minDistance = 2.0f;
        Vector3 headPosition = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;
        Vector3 directionFromHead = (smoothedPosition - headPosition).normalized;
        float currentDistance = Vector3.Distance(smoothedPosition, headPosition);

        if (currentDistance < minDistance)
        {
          Vector3 idealPosition = headPosition + directionFromHead * minDistance;
          float pushBackSpeed = 0.05f;
          smoothedPosition = Vector3.Lerp(smoothedPosition, idealPosition, pushBackSpeed);
        }

        // Only lerp rotation when goal hasn't been scored yet
        if (!goalScored)
        {
          // In third person before goal, only update position - let targeting handle rotation
          camera.transform.position = smoothedPosition;
        }
        else
        {
          camera.transform.position = smoothedPosition;
        }
      }
    }
    private void hideGoalScorer()
    {
      goalScorer.PlayerBody.PlayerMesh.PlayerGroin.gameObject.SetActive(false);
      goalScorer.PlayerBody.PlayerMesh.PlayerTorso.gameObject.SetActive(false);
      goalScorer.PlayerBody.PlayerMesh.PlayerHead.gameObject.SetActive(false);
    }

    private void showGoalScorer()
    {
      goalScorer.PlayerBody.PlayerMesh.PlayerGroin.gameObject.SetActive(true);
      goalScorer.PlayerBody.PlayerMesh.PlayerTorso.gameObject.SetActive(true);
      goalScorer.PlayerBody.PlayerMesh.PlayerHead.gameObject.SetActive(true);
    }
  }
}
