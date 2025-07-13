using UnityEngine;
using UnityEngine.InputSystem;
using System;
using TMPro;
using HarmonyLib;

namespace BetterReplays
{
  public class BetterReplaysHandler : MonoBehaviour
  {
    private const int LOG_THROTTLE_FRAMES = 300;
    private BetterReplaysConfig config;
    private static bool persistedIsFirstPerson = false;
    private static float persistedZoomDistance = 1.0f;
    private Player goalScorer;

    // These are private in PlayerMesh, but we need them to hide and show them when in first or third person
    private TMP_Text goalScorerUsernameText;
    private TMP_Text goalScorerNumberText;
    private float originalUsernameAlpha;
    private float originalNumberAlpha;

    private BaseCamera camera;
    private bool goalScored = false;
    private Goal goal;
    private bool isFirstPerson = false;
    private bool isFreeLook = false;
    private Vector2 freeLookRotation = Vector2.zero;
    private Quaternion originalRotation;
    private float lookSensitivity;
    private bool isTransitioningToFreeLook = false;
    private float freeLookTransitionTime = 0f;
    private Vector3 lastPlayerPosition;
    private Vector3 smoothedPlayerPosition;
    private float currentZoomDistance;
    private bool isTransitioningAfterGoal = false;
    private float goalTransitionTime = 0f;
    private Quaternion goalTransitionStartRotation;

    public void Start()
    {
      config = BetterReplaysConfig.LoadConfig();
      BetterReplaysPlugin.Log("Handler initialized - config loaded and validated");

      if (config.rememberCameraState)
      {
        isFirstPerson = persistedIsFirstPerson;
        currentZoomDistance = persistedZoomDistance;
      }
      else
      {
        currentZoomDistance = config.zoomDefaultDistance;
      }

      lookSensitivity = SettingsManager.Instance.LookSensitivity;
      BetterReplaysPlugin.Log("Better Replays handler initialized with sensitivity: " + lookSensitivity);

      // Destroy this component after 10 seconds
      Destroy(this, 10.0f);
    }

    public void Update()
    {
      if (BetterReplaysConfig.HasConfigChanged())
      {
        config = BetterReplaysConfig.LoadConfig();
        BetterReplaysPlugin.Log("Configuration reloaded due to file changes");
      }

      if (goalScorer == null || camera == null || goal == null)
      {
        if (Time.frameCount % LOG_THROTTLE_FRAMES == 0)
        {
          if (goalScorer == null) BetterReplaysPlugin.LogWarning("Warning: Goal scorer is null in Update()");
          if (camera == null) BetterReplaysPlugin.LogWarning("Warning: Camera is null in Update()");
          if (goal == null) BetterReplaysPlugin.LogWarning("Warning: Goal is null in Update()");
        }
        return;
      }

      // Handle camera toggle input
      bool togglePressed = false;
      if (config.IsToggleCameraMouseButton())
      {
        // Handle mouse button input
        if (config.toggleCameraKey.Equals("leftButton", StringComparison.OrdinalIgnoreCase))
          togglePressed = Mouse.current.leftButton.wasPressedThisFrame;
        else if (config.toggleCameraKey.Equals("rightButton", StringComparison.OrdinalIgnoreCase))
          togglePressed = Mouse.current.rightButton.wasPressedThisFrame;
        else if (config.toggleCameraKey.Equals("middleButton", StringComparison.OrdinalIgnoreCase))
          togglePressed = Mouse.current.middleButton.wasPressedThisFrame;
        else if (config.toggleCameraKey.Equals("forwardButton", StringComparison.OrdinalIgnoreCase))
          togglePressed = Mouse.current.forwardButton.wasPressedThisFrame;
        else if (config.toggleCameraKey.Equals("backButton", StringComparison.OrdinalIgnoreCase))
          togglePressed = Mouse.current.backButton.wasPressedThisFrame;
      }
      else
      {
        // Handle keyboard key input
        Key toggleKey = config.GetToggleCameraKey();
        if (toggleKey != Key.None)
        {
          togglePressed = Keyboard.current[toggleKey].wasPressedThisFrame;
        }
      }

      if (togglePressed)
      {
        isFirstPerson = !isFirstPerson;
        if (config.rememberCameraState)
        {
          persistedIsFirstPerson = isFirstPerson;
        }
        BetterReplaysPlugin.Log("Camera mode switched to: " + (isFirstPerson ? "First Person" : "Third Person"));
      }

      // Handle scroll wheel zoom in third person
      if (!isFirstPerson)
      {
        float scrollInput = Mouse.current.scroll.ReadValue().y;
        if (scrollInput != 0)
        {
          float zoomFactor = scrollInput > 0 ? 0.9f : 1.1f;
          currentZoomDistance *= zoomFactor;
          currentZoomDistance = Mathf.Clamp(currentZoomDistance, 0.0f, config.zoomMaxDistance);

          if (config.rememberCameraState)
          {
            persistedZoomDistance = currentZoomDistance;
          }
        }
      }

      // Handle free look input
      bool freeLookPressed = false;
      bool freeLookReleased = false;

      if (config.IsFreeLookMouseButton())
      {
        // Handle mouse button input
        if (config.freeLookKey.Equals("leftButton", StringComparison.OrdinalIgnoreCase))
        {
          freeLookPressed = Mouse.current.leftButton.wasPressedThisFrame;
          freeLookReleased = Mouse.current.leftButton.wasReleasedThisFrame;
        }
        else if (config.freeLookKey.Equals("rightButton", StringComparison.OrdinalIgnoreCase))
        {
          freeLookPressed = Mouse.current.rightButton.wasPressedThisFrame;
          freeLookReleased = Mouse.current.rightButton.wasReleasedThisFrame;
        }
        else if (config.freeLookKey.Equals("middleButton", StringComparison.OrdinalIgnoreCase))
        {
          freeLookPressed = Mouse.current.middleButton.wasPressedThisFrame;
          freeLookReleased = Mouse.current.middleButton.wasReleasedThisFrame;
        }
        else if (config.freeLookKey.Equals("forwardButton", StringComparison.OrdinalIgnoreCase))
        {
          freeLookPressed = Mouse.current.forwardButton.wasPressedThisFrame;
          freeLookReleased = Mouse.current.forwardButton.wasReleasedThisFrame;
        }
        else if (config.freeLookKey.Equals("backButton", StringComparison.OrdinalIgnoreCase))
        {
          freeLookPressed = Mouse.current.backButton.wasPressedThisFrame;
          freeLookReleased = Mouse.current.backButton.wasReleasedThisFrame;
        }
      }
      else
      {
        // Handle keyboard key input
        Key freeLookKey = config.GetFreeLookKey();
        if (freeLookKey != Key.None)
        {
          freeLookPressed = Keyboard.current[freeLookKey].wasPressedThisFrame;
          freeLookReleased = Keyboard.current[freeLookKey].wasReleasedThisFrame;
        }
      }

      if (freeLookPressed)
      {
        if (config.IsFreeLookToggle())
        {
          bool wasFreeLook = isFreeLook;
          isFreeLook = !isFreeLook;

          if (isFreeLook)
          {
            isTransitioningToFreeLook = true;
            freeLookTransitionTime = 0f;
            originalRotation = camera.transform.rotation;

            Vector3 currentEuler = camera.transform.rotation.eulerAngles;
            freeLookRotation.x = currentEuler.y;
            freeLookRotation.y = currentEuler.x;

            if (freeLookRotation.y > 180f)
            {
              freeLookRotation.y -= 360f;
            }

            Cursor.lockState = CursorLockMode.Locked;
          }
          else if (wasFreeLook)
          {
            isTransitioningToFreeLook = false;
            Cursor.lockState = CursorLockMode.None;
          }
        }
        else
        {
          isFreeLook = true;

          isTransitioningToFreeLook = true;
          freeLookTransitionTime = 0f;
          originalRotation = camera.transform.rotation;

          Vector3 currentEuler = camera.transform.rotation.eulerAngles;
          freeLookRotation.x = currentEuler.y;
          freeLookRotation.y = currentEuler.x;

          if (freeLookRotation.y > 180f)
          {
            freeLookRotation.y -= 360f;
          }

          Cursor.lockState = CursorLockMode.Locked;
        }
      }

      if (!config.IsFreeLookToggle() && freeLookReleased)
      {
        isFreeLook = false;
        isTransitioningToFreeLook = false;
        Cursor.lockState = CursorLockMode.None;
      }

      ReplayCamera rc = camera as ReplayCamera;
      if (rc == null)
      {
        BetterReplaysPlugin.LogError("Error: Camera is not a ReplayCamera type");
        return;
      }

      if (!isFirstPerson)
      {
        // Before goal is scored, target the goal. After goal is scored, target the player
        if (goalScored || isFreeLook)
        {
          if (goalScorer.PlayerCamera?.transform != null)
          {
            rc.SetTarget(goalScorer.PlayerCamera.transform);
          }
          else
          {
            BetterReplaysPlugin.LogWarning("Warning: Goal scorer PlayerCamera transform is null");
          }
        }
        else
        {
          if (goal.transform != null)
          {
            rc.SetTarget(goal.transform);
          }
          else
          {
            BetterReplaysPlugin.LogWarning("Warning: Goal transform is null");
          }
        }
      }
      else
      {
        rc.SetTarget(null);
      }

      Vector3 targetPosition;

      try
      {
        targetPosition = goalScorer.PlayerCamera.transform.position;
      }
      catch (System.NullReferenceException)
      {
        BetterReplaysPlugin.LogError("Error: Goal scorer PlayerCamera transform is null when getting position");
        return;
      }

      // For third person, adjust target position based on replay phase
      if (!isFirstPerson)
      {
        try
        {
          Vector3 currentPlayerPosition = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;

          if (lastPlayerPosition == Vector3.zero)
          {
            lastPlayerPosition = currentPlayerPosition;
            smoothedPlayerPosition = currentPlayerPosition;
          }
          else
          {
            smoothedPlayerPosition = Vector3.Lerp(smoothedPlayerPosition, currentPlayerPosition, config.playerPositionSmoothing);
          }
          lastPlayerPosition = currentPlayerPosition;
          if (goalScored || isFreeLook)
          {
            Vector3 goalToPlayerDirection = (smoothedPlayerPosition - goal.transform.position).normalized;
            float safeZoomDistance = Mathf.Max(0f, currentZoomDistance);

            float heightMultiplier = Mathf.Lerp(1.0f, 0.5f, (safeZoomDistance - config.zoomDefaultDistance) / (config.zoomMaxDistance - config.zoomDefaultDistance));
            heightMultiplier = Mathf.Clamp(heightMultiplier, 0.5f, 1.0f);
            float dynamicHeight = config.cameraOffsetHeight * heightMultiplier;

            Vector3 offset = goalToPlayerDirection * safeZoomDistance + Vector3.up * dynamicHeight;
            targetPosition = smoothedPlayerPosition + offset;
          }
          else
          {
            Vector3 goalToPlayerDirection = (smoothedPlayerPosition - goal.transform.position).normalized;

            float safeZoomDistance = Mathf.Max(0f, currentZoomDistance);

            float heightMultiplier = Mathf.Lerp(1.0f, 0.5f, (safeZoomDistance - config.zoomDefaultDistance) / (config.zoomMaxDistance - config.zoomDefaultDistance));
            heightMultiplier = Mathf.Clamp(heightMultiplier, 0.5f, 1.0f);
            float dynamicHeight = config.cameraOffsetHeight * heightMultiplier;

            Vector3 offset = goalToPlayerDirection * safeZoomDistance + Vector3.up * dynamicHeight;
            targetPosition = smoothedPlayerPosition + offset;
          }
        }
        catch (System.NullReferenceException)
        {
          BetterReplaysPlugin.LogError("Error: Goal scorer player mesh components are null when calculating third person position");
          return;
        }
      }

      Quaternion targetRotation;
      try
      {
        targetRotation = goalScorer.PlayerCamera.transform.rotation;
      }
      catch (System.NullReferenceException)
      {
        BetterReplaysPlugin.LogError("Error: Goal scorer PlayerCamera transform is null when getting rotation");
        return;
      }

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
      try
      {
        if (isFreeLook)
        {
          Vector2 mouseDelta = Mouse.current.delta.ReadValue();
          freeLookRotation.x += mouseDelta.x * this.lookSensitivity;
          freeLookRotation.y -= mouseDelta.y * this.lookSensitivity;
          freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, -config.firstPersonPitchLimit, config.firstPersonPitchLimit);

          Quaternion freeLookQuat = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
          camera.transform.SetPositionAndRotation(targetPosition, freeLookQuat);
        }
        else
        {
          float rotationLerpSpeed = config.firstPersonRotationLerpSpeed;
          Quaternion smoothedRotation = Quaternion.Lerp(camera.transform.rotation, targetRotation, rotationLerpSpeed);
          camera.transform.SetPositionAndRotation(targetPosition, smoothedRotation);
        }

        HideGoalScorer();
      }
      catch (System.Exception e)
      {
        BetterReplaysPlugin.LogError($"Error in HandleFirstPersonCamera: {e.Message}");
      }
    }

    private void HandleThirdPersonCamera(Vector3 targetPosition, Quaternion targetRotation)
    {
      try
      {
        float lerpSpeed;
        if (currentZoomDistance <= config.zoomDefaultDistance)
        {
          float t = (currentZoomDistance - config.zoomMinDistance) / (config.zoomDefaultDistance - config.zoomMinDistance);
          lerpSpeed = Mathf.Lerp(config.lerpSpeedAtMinZoom, config.lerpSpeedAtDefaultZoom, t);
        }
        else
        {
          float t = (currentZoomDistance - config.zoomDefaultDistance) / (config.zoomMaxDistance - config.zoomDefaultDistance);
          lerpSpeed = Mathf.Lerp(config.lerpSpeedAtDefaultZoom, config.lerpSpeedAtMaxZoom, t);
        }

        Vector3 smoothedPosition;

        if (isFreeLook)
        {
          Vector2 mouseDelta = Mouse.current.delta.ReadValue();
          freeLookRotation.x += mouseDelta.x * lookSensitivity;
          freeLookRotation.y -= mouseDelta.y * lookSensitivity;
          freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, config.freeLookPitchMin, config.freeLookPitchMax);

          Vector3 playerHead = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;
          float distance = currentZoomDistance;

          Quaternion orbitalRotation = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
          Vector3 orbitalPosition = playerHead + orbitalRotation * Vector3.back * distance;

          smoothedPosition = Vector3.Lerp(camera.transform.position, orbitalPosition, config.freeLookOrbitalLerpSpeed);

          Vector3 lookDirection = (playerHead - smoothedPosition).normalized;
          Quaternion targetLookRotation = Quaternion.LookRotation(lookDirection);

          if (isTransitioningToFreeLook)
          {
            freeLookTransitionTime += Time.deltaTime;
            float transitionDuration = config.freeLookTransitionDuration;
            float t = Mathf.Clamp01(freeLookTransitionTime / transitionDuration);

            Quaternion smoothedRotation = Quaternion.Lerp(originalRotation, targetLookRotation, t);
            camera.transform.SetPositionAndRotation(smoothedPosition, smoothedRotation);

            if (t >= 1f)
            {
              isTransitioningToFreeLook = false;
            }
          }
          else
          {
            camera.transform.SetPositionAndRotation(smoothedPosition, targetLookRotation);
          }
        }
        else
        {
          // Normal third person mode
          Quaternion targetThirdPersonRotation;
          if (goalScored)
          {
            // After goal is scored, look at the player
            Vector3 lookDirection = (smoothedPlayerPosition - targetPosition).normalized;
            targetThirdPersonRotation = Quaternion.LookRotation(lookDirection);
          }
          else
          {
            // Before goal scored, look at the goal
            Vector3 lookDirection = (goal.transform.position - targetPosition).normalized;
            targetThirdPersonRotation = Quaternion.LookRotation(lookDirection);
          }
          Quaternion finalRotation = targetThirdPersonRotation;
          if (isTransitioningAfterGoal)
          {
            goalTransitionTime += Time.deltaTime;
            finalRotation = Quaternion.Lerp(goalTransitionStartRotation, targetThirdPersonRotation, config.goalToPlayerRotationLerpSpeed);

            if (Quaternion.Angle(finalRotation, targetThirdPersonRotation) < 1f)
            {
              isTransitioningAfterGoal = false;
            }
          }

          // Apply single lerp to reach target position and rotation
          Vector3 finalPosition = Vector3.Lerp(camera.transform.position, targetPosition, lerpSpeed);
          finalRotation = Quaternion.Lerp(camera.transform.rotation, finalRotation, lerpSpeed);

          // Apply push back if camera is too close to player
          if (currentZoomDistance >= config.minCameraDistance)
          {
            Vector3 headPosition = smoothedPlayerPosition;
            Vector3 directionFromHead = (finalPosition - headPosition).normalized;
            float currentDistance = Vector3.Distance(finalPosition, headPosition);

            if (currentDistance < config.minCameraDistance)
            {
              Vector3 idealPosition = headPosition + directionFromHead * config.minCameraDistance;
              float pushBackSpeed = config.pushBackSpeed;
              finalPosition = Vector3.Lerp(finalPosition, idealPosition, pushBackSpeed);
            }
          }

          camera.transform.SetPositionAndRotation(finalPosition, finalRotation);
        }

        ShowGoalScorer();
      }
      catch (System.Exception e)
      {
        BetterReplaysPlugin.LogError($"Error in HandleThirdPersonCamera: {e.Message}");
      }
    }

    // Initialize camera position behind goal scorer
    private void InitializeCameraPosition()
    {
      if (goalScorer == null || camera == null)
      {
        BetterReplaysPlugin.LogWarning("Warning: Cannot initialize camera position: goalScorer or camera is null");
        return;
      }

      try
      {
        Vector3 playerPosition = goalScorer.PlayerCamera.transform.position;
        Vector3 playerForward = goalScorer.PlayerCamera.transform.forward;

        Vector3 initialPosition = playerPosition - playerForward * config.initialCameraDistance;

        camera.transform.position = initialPosition;
        camera.transform.rotation = goalScorer.PlayerCamera.transform.rotation;
      }
      catch (System.NullReferenceException e)
      {
        BetterReplaysPlugin.LogError($"Failed to initialize camera position due to null reference: {e.Message}");
      }
    }

    private void HideGoalScorer()
    {
      try
      {
        if (goalScorer?.PlayerBody?.PlayerMesh != null)
        {
          goalScorer.PlayerBody.PlayerMesh.PlayerGroin.gameObject.SetActive(false);
          goalScorer.PlayerBody.PlayerMesh.PlayerTorso.gameObject.SetActive(false);
          goalScorer.PlayerBody.PlayerMesh.PlayerHead.gameObject.SetActive(false);
          goalScorerUsernameText.alpha = 0f;
          goalScorerNumberText.alpha = 0f;
        }
        else
        {
          BetterReplaysPlugin.LogWarning("Warning: Cannot hide goal scorer: player mesh components are null");
        }
      }
      catch (System.Exception e)
      {
        BetterReplaysPlugin.LogError($"Error hiding goal scorer: {e.Message}");
      }
    }

    private void ShowGoalScorer()
    {
      try
      {
        if (goalScorer?.PlayerBody?.PlayerMesh != null)
        {
          goalScorer.PlayerBody.PlayerMesh.PlayerGroin.gameObject.SetActive(true);
          goalScorer.PlayerBody.PlayerMesh.PlayerTorso.gameObject.SetActive(true);
          goalScorer.PlayerBody.PlayerMesh.PlayerHead.gameObject.SetActive(true);
          goalScorerUsernameText.alpha = originalUsernameAlpha;
          goalScorerNumberText.alpha = originalNumberAlpha;
        }
        else
        {
          BetterReplaysPlugin.LogWarning("Warning: Cannot show goal scorer: player mesh components are null");
        }
      }
      catch (System.Exception e)
      {
        BetterReplaysPlugin.LogError($"Error showing goal scorer: {e.Message}");
      }
    }

    public void SetGoal(Goal goal)
    {
      this.goal = goal;
    }

    public void SetGoalScorer(Player goalScorer)
    {
      if (goalScorer == null)
      {
        BetterReplaysPlugin.LogWarning("Warning: Attempted to set null goal scorer");
        return;
      }

      foreach (Player replayPlayerSearch in PlayerManager.Instance.GetReplayPlayers())
      {
        if (replayPlayerSearch.Username.Value.ToString() == goalScorer.Username.Value.ToString() &&
            replayPlayerSearch.Number.Value.ToString() == goalScorer.Number.Value.ToString())
        {
          this.goalScorer = replayPlayerSearch;
          // Since the username text and number text for a player is private, we need to use Traverse to obtain them for hiding and showing later.
          if (this.goalScorer?.PlayerBody?.PlayerMesh != null)
          {
            var playerMeshTraverse = Traverse.Create(this.goalScorer.PlayerBody.PlayerMesh);

            var usernameTextField = playerMeshTraverse.Field("usernameText").GetValue<TMP_Text>();
            var numberTextField = playerMeshTraverse.Field("numberText").GetValue<TMP_Text>();

            goalScorerUsernameText = usernameTextField;
            goalScorerNumberText = numberTextField;

            originalUsernameAlpha = goalScorerUsernameText.alpha;
            originalNumberAlpha = goalScorerNumberText.alpha;
          }
          else
          {
            BetterReplaysPlugin.LogWarning("PlayerMesh is null when trying to set goal scorer");
          }
          break;
        }
      }

      if (this.goalScorer == null)
      {
        BetterReplaysPlugin.LogError("Error: Could not locate replay player for goal scorer!");
      }
      else
      {
        BetterReplaysPlugin.Log("Goal scorer set: " + this.goalScorer.Username.Value + " (" + this.goalScorer.Number.Value + ")");
        if (camera != null)
        {
          InitializeCameraPosition();
        }
      }
    }

    public void SetReplayCamera(BaseCamera camera)
    {
      if (camera == null)
      {
        BetterReplaysPlugin.LogError("Error: Attempted to set null replay camera");
        return;
      }

      this.camera = camera;
      BetterReplaysPlugin.Log("Replay camera configured with FOV: " + SettingsManager.Instance.Fov);

      try
      {
        camera.CameraComponent.usePhysicalProperties = false;
        camera.CameraComponent.fieldOfView = SettingsManager.Instance.Fov;
      }
      catch (System.Exception e)
      {
        BetterReplaysPlugin.LogError($"Error configuring camera settings: {e.Message}");
      }

      if (goalScorer != null)
      {
        InitializeCameraPosition();
      }
    }

    public void SetGoalScored(bool goalScored)
    {
      this.goalScored = goalScored;
      if (goalScored)
      {
        BetterReplaysPlugin.Log("Goal scored flag set - camera will now target goal scorer");

        // Initiate rotation transition from goal-focused to player-focused
        if (camera != null)
        {
          isTransitioningAfterGoal = true;
          goalTransitionTime = 0f;
          goalTransitionStartRotation = camera.transform.rotation;
        }
      }
    }

    public void SetGoalScorerUsernameText(TMP_Text goalScorerUsernameText)
    {
      this.goalScorerUsernameText = goalScorerUsernameText;
    }

    public void SetGoalScorerNumberText(TMP_Text goalScorerNumberText)
    {
      this.goalScorerNumberText = goalScorerNumberText;
    }

  }
}
