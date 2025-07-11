using UnityEngine;
using UnityEngine.InputSystem;
using System;
using TMPro;
using HarmonyLib;

namespace BetterReplays
{
  public class BetterReplaysHandler : MonoBehaviour
  {
    // Constants for camera behavior
    private const float PLAYER_POSITION_SMOOTHING = 0.3f;
    private const float PUSH_BACK_SPEED = 0.01f;
    private const float FREE_LOOK_ORBITAL_LERP_SPEED = 0.05f;
    private const float MIN_CAMERA_DISTANCE = 2.0f;
    private const float FREE_LOOK_TRANSITION_DURATION = 0.5f;
    private const float COMPONENT_DESTROY_TIME = 10.0f;
    private const int LOG_THROTTLE_FRAMES = 300;
    private const float FIRST_PERSON_ROTATION_LERP_SPEED = 0.1f;
    private const float CAMERA_OFFSET_HEIGHT = 0.5f;
    private const float INITIAL_CAMERA_DISTANCE = 3.0f;
    private const float FIRST_PERSON_PITCH_LIMIT = 90f;
    private const float FREE_LOOK_PITCH_MIN = -20f;
    private const float FREE_LOOK_PITCH_MAX = 80f;
    private const float ZOOM_MIN_DISTANCE = 0.2f;
    private const float ZOOM_MAX_DISTANCE = 16.0f;
    private const float ZOOM_DEFAULT_DISTANCE = 1.0f;
    private const float LERP_SPEED_AT_MIN_ZOOM = 0.1f;
    private const float LERP_SPEED_AT_MAX_ZOOM = 0.01f;
    private const float LERP_SPEED_AT_DEFAULT_ZOOM = 0.02f;

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
    private float currentZoomDistance = ZOOM_DEFAULT_DISTANCE;

    public void Start()
    {
      lookSensitivity = SettingsManager.Instance.LookSensitivity;
      BetterReplaysPlugin.Log("Better Replays handler initialized with sensitivity: " + lookSensitivity);

      // Destroy this component after 10 seconds
      Destroy(this, COMPONENT_DESTROY_TIME);
    }

    public void Update()
    {
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

      if (Keyboard.current.cKey.wasPressedThisFrame)
      {
        isFirstPerson = !isFirstPerson;
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
          currentZoomDistance = Mathf.Clamp(currentZoomDistance, ZOOM_MIN_DISTANCE, ZOOM_MAX_DISTANCE);
        }
      }

      if (Mouse.current.rightButton.wasPressedThisFrame)
      {
        isFreeLook = true;
        isTransitioningToFreeLook = true;
        freeLookTransitionTime = 0f;
        originalRotation = camera.transform.rotation;

        Vector3 currentEuler = camera.transform.rotation.eulerAngles;
        freeLookRotation.x = currentEuler.y;
        freeLookRotation.y = currentEuler.x;

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

      ReplayCamera rc = camera as ReplayCamera;
      if (rc == null)
      {
        BetterReplaysPlugin.LogError("Error: Camera is not a ReplayCamera type");
        return;
      }

      if (!isFirstPerson)
      {
        // In free look mode or after goal scored, target the goal scorer
        if (isFreeLook || goalScored)
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

      // For third person, adjust target position to be above and behind player
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
            smoothedPlayerPosition = Vector3.Lerp(smoothedPlayerPosition, currentPlayerPosition, PLAYER_POSITION_SMOOTHING);
          }
          lastPlayerPosition = currentPlayerPosition;

          Vector3 playerForward = goalScorer.PlayerCamera.transform.forward;

          // Lower camera height as zoom increases
          float heightMultiplier = Mathf.Lerp(1.0f, 0.5f, (currentZoomDistance - ZOOM_DEFAULT_DISTANCE) / (ZOOM_MAX_DISTANCE - ZOOM_DEFAULT_DISTANCE));
          heightMultiplier = Mathf.Clamp(heightMultiplier, 0.5f, 1.0f);
          float dynamicHeight = CAMERA_OFFSET_HEIGHT * heightMultiplier;

          // Position camera above and behind player
          Vector3 offset = -playerForward * currentZoomDistance + Vector3.up * dynamicHeight;
          targetPosition = smoothedPlayerPosition + offset;
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
          freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, -FIRST_PERSON_PITCH_LIMIT, FIRST_PERSON_PITCH_LIMIT);

          Quaternion freeLookQuat = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
          camera.transform.SetPositionAndRotation(targetPosition, freeLookQuat);
        }
        else
        {
          float rotationLerpSpeed = FIRST_PERSON_ROTATION_LERP_SPEED;
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
        if (currentZoomDistance <= ZOOM_DEFAULT_DISTANCE)
        {
          float t = (currentZoomDistance - ZOOM_MIN_DISTANCE) / (ZOOM_DEFAULT_DISTANCE - ZOOM_MIN_DISTANCE);
          lerpSpeed = Mathf.Lerp(LERP_SPEED_AT_MIN_ZOOM, LERP_SPEED_AT_DEFAULT_ZOOM, t);
        }
        else
        {
          float t = (currentZoomDistance - ZOOM_DEFAULT_DISTANCE) / (ZOOM_MAX_DISTANCE - ZOOM_DEFAULT_DISTANCE);
          lerpSpeed = Mathf.Lerp(LERP_SPEED_AT_DEFAULT_ZOOM, LERP_SPEED_AT_MAX_ZOOM, t);
        }

        Vector3 smoothedPosition;

        if (isFreeLook)
        {
          Vector2 mouseDelta = Mouse.current.delta.ReadValue();
          freeLookRotation.x += mouseDelta.x * lookSensitivity;
          freeLookRotation.y -= mouseDelta.y * lookSensitivity;
          freeLookRotation.y = Mathf.Clamp(freeLookRotation.y, FREE_LOOK_PITCH_MIN, FREE_LOOK_PITCH_MAX);

          Vector3 playerHead = goalScorer.PlayerBody.PlayerMesh.PlayerHead.transform.position;
          float distance = currentZoomDistance;

          Quaternion orbitalRotation = Quaternion.Euler(freeLookRotation.y, freeLookRotation.x, 0f);
          Vector3 orbitalPosition = playerHead + orbitalRotation * Vector3.back * distance;

          smoothedPosition = Vector3.Lerp(camera.transform.position, orbitalPosition, FREE_LOOK_ORBITAL_LERP_SPEED);

          Vector3 lookDirection = (playerHead - smoothedPosition).normalized;
          Quaternion targetLookRotation = Quaternion.LookRotation(lookDirection);

          if (isTransitioningToFreeLook)
          {
            freeLookTransitionTime += Time.deltaTime;
            float transitionDuration = FREE_LOOK_TRANSITION_DURATION;
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
          smoothedPosition = Vector3.Lerp(camera.transform.position, targetPosition, lerpSpeed);

          if (currentZoomDistance >= MIN_CAMERA_DISTANCE)
          {
            Vector3 headPosition = smoothedPlayerPosition;
            Vector3 directionFromHead = (smoothedPosition - headPosition).normalized;
            float currentDistance = Vector3.Distance(smoothedPosition, headPosition);

            if (currentDistance < MIN_CAMERA_DISTANCE)
            {
              Vector3 idealPosition = headPosition + directionFromHead * MIN_CAMERA_DISTANCE;
              float pushBackSpeed = PUSH_BACK_SPEED;
              smoothedPosition = Vector3.Lerp(smoothedPosition, idealPosition, pushBackSpeed);
            }
          }

          camera.transform.position = smoothedPosition;

          ShowGoalScorer();
        }
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

        Vector3 initialPosition = playerPosition - playerForward * INITIAL_CAMERA_DISTANCE;

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
            BetterReplaysPlugin.Log("PlayerMesh is not null");
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
            BetterReplaysPlugin.Log("PlayerMesh is null");
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
