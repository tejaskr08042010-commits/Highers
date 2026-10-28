using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;
using System.Collections.Generic;

public class BasicMovement_2 : MonoBehaviour
{
    [Header("Flight Settings")]
    public float acceleration = 50f;
    public float deceleration = 30f;
    public float mouseSensitivity = 2f;
    [Range(0.01f, 0.5f)]
    public float cameraSmoothing = 0.1f;
    public float verticalSpeed = 50f;

    [Header("Lock-On Settings")]
    [Tooltip("If true, walls will block target lock-on. Set to false for pure screen-space targeting anywhere in the arena.")]
    public bool requireLineOfSight = false;
    public LayerMask lineOfSightMask = ~0;

    [Header("Lock-On UI Settings")]
    [Range(2f, 100f)]
    public float reticleSize = 12f;
    [Tooltip("Optional: Assign a custom circle/dot image here. If left empty, it will draw a simple square dot.")]
    public Texture2D customReticleTexture;

    [Header("Boost Settings")]
    public float boostAcceleration = 500f;
    public float boostManaCostPerSecond = 3f;
    public float boostMinimumMana = 2.1f;

    [Header("High-Speed Acceleration Settings")]
    public float maxNormalSpeed = 10000f;
    public float accelerationThreshold = 10000f;

    [Header("Velocity Safety Settings")]
    public float maxSafeSpeed = 15000f;
    public float damageThreshold = 10000f;

    [Header("Instant Stop Settings")]
    public float instantStopManaCost = 5f;

    [Header("Instant Turn Settings")]
    public float instantTurnManaCost = 7f;

    [Header("Teleport Settings")]
    public float teleportDistance = 50f;
    public float maxTeleportRange = 200f;
    public float teleportChargeTime = 2f;
    public float minTeleportManaCost = 10f;
    public float maxTeleportManaCost = 100f;
    public float teleportCooldown = 0.5f;

    [Header("Inside-Object Detection")]
    public LayerMask collisionMask = ~0;
    public float insideCheckRayDistance = 1000000f;

    [Header("Object Intersection Effects")]
    public PlayerHealth healthScript;
    public Volume globalVolume;
    public GameObject cameraObject;
    public float basePostExposure = 0.5f;
    public float postExposureInside = -5f;
    public float insideObjectDamagePerSecond = 100f;

    [Header("Teleport Flash Settings")]
    [Tooltip("Time in seconds (z) to lerp exposure from 1.5 to 0.5 during the operator's teleport.")]
    public float teleportFlashDuration = 0.5f;

    [Header("Mana System")]
    public AdvanceManaSystem manaSystem;

    [HideInInspector] public bool movementEnabled = true;
    [HideInInspector] public bool lockInput = false;
    public bool noInput = false;
    [HideInInspector] public bool useUnscaledTime = false;

    [HideInInspector] public bool isLockedOn = false;
    [HideInInspector] public Transform currentLockedTarget;

    private Vector3 lockOnReferenceUp = Vector3.up;
    private Vector3 currentVelocity = Vector3.zero;
    private float lastTeleportTime = -999f;
    private bool canTeleport => Time.unscaledTime >= lastTeleportTime + teleportCooldown;
    private bool isBoosting = false;

    private float teleportRange;
    private float currentChargeTime = 0f;
    private bool isChargingTeleport = false;
    private bool teleportChargeCancelled = false;
    private Vector3 intendedTeleportDirection;

    private Quaternion targetRotation;
    private Quaternion currentRotation;
    private SphereCollider playerCollider;

    private Transform cameraTransform;
    private Camera cachedCamera;

    private bool isInsideObject = false;
    private float effectLerpProgress = 0f;
    private float velocityBreathAdd = 0f;

    private ColorAdjustments colorAdjustments;

    private bool isFlashActive = false;
    private float flashExposureOverride = 0f;
    private Coroutine flashCoroutine = null;

    private CameraShake breathScript;
    private float initialBreathIntensity;

    private RaycastHit[] hitBuffer = new RaycastHit[512];
    private List<MonoBehaviour> disabledScripts = new List<MonoBehaviour>();

    void Start()
    {
        if (cameraObject == null)
            cameraObject = Camera.main?.gameObject;

        if (cameraObject != null)
        {
            cameraTransform = cameraObject.transform;
            cachedCamera = cameraObject.GetComponent<Camera>();

            targetRotation = cameraTransform.rotation;
            currentRotation = cameraTransform.rotation;

            breathScript = cameraObject.GetComponent<CameraShake>();
            if (breathScript != null)
                initialBreathIntensity = breathScript.breathIntensity;
        }

        if (globalVolume != null)
            globalVolume.profile.TryGet(out colorAdjustments);

        if (manaSystem == null) manaSystem = GetComponent<AdvanceManaSystem>();
        if (healthScript == null) healthScript = GetComponent<PlayerHealth>();

        playerCollider = GetComponent<SphereCollider>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        teleportRange = teleportDistance;
    }

    void Update()
    {
        if (!lockInput && !noInput)
        {
            HandleLockOnSystem();
            HandleInstantStop();
            HandleInstantTurn();
            HandleCameraOnlyTurn();
            HandleTeleport();
        }
        else
        {
            isChargingTeleport = false;
        }

        HandleMovement();
        HandleVelocitySafetyEffects();

        CheckIfInsideObject();
        HandleInsideObjectEffects();
    }

    void LateUpdate()
    {
        if (!lockInput && !noInput)
            HandleCameraRotation();
    }

    float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    private void HandleLockOnSystem()
    {
        if (Input.GetMouseButtonDown(2))
        {
            if (isLockedOn)
            {
                isLockedOn = false;
                currentLockedTarget = null;
            }
            else
            {
                lockOnReferenceUp = currentRotation * Vector3.up;
                FindAndLockTarget();
            }
        }

        if (isLockedOn && (currentLockedTarget == null || !currentLockedTarget.gameObject.activeInHierarchy))
        {
            FindAndLockTarget();
        }
    }

    private void FindAndLockTarget()
    {
        Lockable bestTarget = null;
        float bestDistSq = float.MaxValue;
        Camera cam = cachedCamera != null ? cachedCamera : Camera.main;
        if (cam == null) return;

        foreach (Lockable lockable in Lockable.AllLockables)
        {
            if (lockable == null || !lockable.gameObject.activeInHierarchy) continue;

            Vector3 targetPos = lockable.TargetPosition;
            Vector3 vp = cam.WorldToViewportPoint(targetPos);

            if (vp.z > 0)
            {
                if (requireLineOfSight)
                {
                    Vector3 dirToTarget = targetPos - cameraTransform.position;
                    float dist = dirToTarget.magnitude;

                    if (Physics.Raycast(cameraTransform.position, dirToTarget.normalized, out RaycastHit hit, dist, lineOfSightMask))
                    {
                        if (hit.collider.gameObject != lockable.gameObject && !hit.collider.transform.IsChildOf(lockable.transform))
                        {
                            continue;
                        }
                    }
                }

                Vector2 centerDist = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                float distSq = centerDist.sqrMagnitude;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestTarget = lockable;
                }
            }
        }

        if (bestTarget != null)
        {
            currentLockedTarget = bestTarget.lockOnPoint != null ? bestTarget.lockOnPoint : bestTarget.transform;
            isLockedOn = true;
        }
        else
        {
            isLockedOn = false;
            currentLockedTarget = null;
        }
    }

    void HandleCameraRotation()
    {
        if (isLockedOn && currentLockedTarget != null)
        {
            Vector3 directionToTarget = currentLockedTarget.position - cameraTransform.position;
            if (directionToTarget.sqrMagnitude > 0.001f)
            {
                targetRotation = Quaternion.LookRotation(directionToTarget.normalized, lockOnReferenceUp);
            }
        }
        else
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            targetRotation = Quaternion.AngleAxis(mouseX, targetRotation * Vector3.up) * targetRotation;
            targetRotation *= Quaternion.AngleAxis(-mouseY, Vector3.right);
        }

        float t = 1f - Mathf.Exp(-DeltaTime / cameraSmoothing);
        currentRotation = Quaternion.Slerp(currentRotation, targetRotation, t);

        cameraTransform.rotation = currentRotation;
        transform.rotation = currentRotation;
    }

    void OnGUI()
    {
        if (isLockedOn && currentLockedTarget != null)
        {
            Camera cam = cachedCamera != null ? cachedCamera : Camera.main;
            if (cam == null) return;

            Vector3 screenPos = cam.WorldToScreenPoint(currentLockedTarget.position);

            if (screenPos.z > 0)
            {
                float guiY = Screen.height - screenPos.y;
                GUI.color = Color.red;

                Texture2D texToDraw = customReticleTexture != null ? customReticleTexture : Texture2D.whiteTexture;
                float halfSize = reticleSize / 2f;

                GUI.DrawTexture(new Rect(screenPos.x - halfSize, guiY - halfSize, reticleSize, reticleSize), texToDraw);
            }
        }
    }

    private void HandleInsideObjectEffects()
    {
        if (isInsideObject)
        {
            effectLerpProgress += DeltaTime / 2f;
            if (healthScript != null) healthScript.TakeDamage(insideObjectDamagePerSecond * DeltaTime);
        }
        else effectLerpProgress -= DeltaTime / 0.5f;

        effectLerpProgress = Mathf.Clamp01(effectLerpProgress);
        ApplyVisualEffects(effectLerpProgress);
    }

    private void ApplyVisualEffects(float progress)
    {
        if (colorAdjustments != null)
        {
            if (isFlashActive)
            {
                colorAdjustments.postExposure.value = flashExposureOverride;
            }
            else
            {
                colorAdjustments.postExposure.value = Mathf.Lerp(basePostExposure, postExposureInside, progress);
            }
        }

        if (breathScript != null) breathScript.breathIntensity = Mathf.Lerp(initialBreathIntensity, 1f, progress) + velocityBreathAdd;
    }

    public void ResetEffectsOnDeath()
    {
        isInsideObject = false;
        effectLerpProgress = 0f;
        velocityBreathAdd = 0f;
        ApplyVisualEffects(0f);
    }

    private bool HasEnoughTotalMana(float amount) => manaSystem != null && manaSystem.CurrentTotalMana >= amount;

    private bool UseTotalMana(float amount)
    {
        if (HasEnoughTotalMana(amount)) { manaSystem.DrainTotalReserves(amount); return true; }
        return false;
    }

    void HandleInstantStop()
    {
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
        {
            if (HasEnoughTotalMana(instantStopManaCost) && UseTotalMana(instantStopManaCost))
                currentVelocity = Vector3.zero;
        }
    }

    void HandleInstantTurn()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (HasEnoughTotalMana(instantTurnManaCost) && UseTotalMana(instantTurnManaCost))
            {
                float currentSpeed = currentVelocity.magnitude;
                currentVelocity = cameraTransform.forward * currentSpeed;
            }
        }
    }

    void HandleCameraOnlyTurn()
    {
        if (Input.GetKeyDown(KeyCode.X))
        {
            Quaternion flip = Quaternion.AngleAxis(180f, targetRotation * Vector3.up);
            targetRotation = flip * targetRotation;
            currentRotation = flip * currentRotation;
        }
    }

    private Vector3 GetPlayerInputDirection()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float forward = Input.GetAxisRaw("Vertical");
        float vertical = 0f;

        if (Input.GetKey(KeyCode.Q)) vertical = -1f;
        if (Input.GetKey(KeyCode.E)) vertical = 1f;

        Vector3 inputDir = cameraTransform.forward * forward + cameraTransform.right * horizontal + cameraTransform.up * vertical;
        return inputDir.magnitude > 0.1f ? inputDir.normalized : Vector3.zero;
    }

    void HandleMovement()
    {
        if (lockInput)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        Vector3 inputDirection = noInput ? Vector3.zero : GetPlayerInputDirection();

        bool wantsToBoost = !noInput && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && inputDirection.magnitude > 0.1f;
        bool hasEnoughManaToBoost = manaSystem != null && manaSystem.CurrentTotalMana >= boostMinimumMana;

        if (wantsToBoost && hasEnoughManaToBoost) isBoosting = UseTotalMana(boostManaCostPerSecond * DeltaTime);
        else isBoosting = false;

        float currentAcceleration = isBoosting ? boostAcceleration : acceleration;

        if (inputDirection.magnitude > 0.1f)
        {
            float parallelSpeed = Vector3.Dot(currentVelocity, inputDirection);
            Vector3 perpVelocity = currentVelocity - inputDirection * parallelSpeed;
            float speedForDecay = Mathf.Max(0f, parallelSpeed);

            if (speedForDecay > maxNormalSpeed)
            {
                float excessSpeed = speedForDecay - maxNormalSpeed;
                currentAcceleration *= Mathf.Exp(-excessSpeed / accelerationThreshold);
            }

            float newParallelSpeed = parallelSpeed + currentAcceleration * DeltaTime;

            currentVelocity = inputDirection * newParallelSpeed + perpVelocity;
        }
        else currentVelocity = Vector3.MoveTowards(currentVelocity, Vector3.zero, deceleration * DeltaTime);

        transform.position += currentVelocity * DeltaTime;
    }

    private void HandleVelocitySafetyEffects()
    {
        float speed = currentVelocity.magnitude;
        if (speed > maxSafeSpeed)
        {
            velocityBreathAdd = (speed - maxSafeSpeed) / 10000f;
            if (healthScript != null)
            {
                float excessSpeed = speed - maxSafeSpeed;
                healthScript.TakeDamage((excessSpeed * excessSpeed / damageThreshold) * DeltaTime);
            }
        }
        else velocityBreathAdd = 0f;
    }

    void HandleTeleport()
    {
        if (Input.GetKeyDown(KeyCode.Space) && canTeleport)
        {
            isChargingTeleport = true;
            currentChargeTime = 0f;
            teleportChargeCancelled = false;
            teleportRange = teleportDistance;

            Vector3 currentInput = GetPlayerInputDirection();
            intendedTeleportDirection = currentInput != Vector3.zero ? currentInput : cameraTransform.forward;
        }

        if (isChargingTeleport)
        {
            if (Input.GetKeyDown(KeyCode.C))
            {
                isChargingTeleport = false;
                teleportChargeCancelled = true;
                teleportRange = teleportDistance;
            }
            else
            {
                Vector3 currentInput = GetPlayerInputDirection();
                if (currentInput != Vector3.zero) intendedTeleportDirection = currentInput;

                currentChargeTime += DeltaTime;
                currentChargeTime = Mathf.Min(currentChargeTime, teleportChargeTime);

                float progress = teleportChargeTime > 0f ? currentChargeTime / teleportChargeTime : 0f;
                float expProgress = (Mathf.Pow(4f, progress) - 1f) / 3f;
                teleportRange = Mathf.Lerp(teleportDistance, maxTeleportRange, expProgress);
            }
        }

        if (Input.GetKeyUp(KeyCode.Space))
        {
            if (teleportChargeCancelled) { teleportChargeCancelled = false; return; }

            if (isChargingTeleport)
            {
                isChargingTeleport = false;

                float progress = teleportChargeTime > 0f ? currentChargeTime / teleportChargeTime : 0f;
                float expProgress = (Mathf.Pow(4f, progress) - 1f) / 3f;
                float finalManaCost = Mathf.Lerp(minTeleportManaCost, maxTeleportManaCost, expProgress);

                if (!HasEnoughTotalMana(finalManaCost)) return;

                ExecuteDirectionalTeleport(finalManaCost);
            }
        }
    }

    void ExecuteDirectionalTeleport(float finalManaCost)
    {
        Vector3 targetPosition = transform.position + intendedTeleportDirection * teleportRange;

        if (!UseTotalMana(finalManaCost)) return;

        transform.position = targetPosition;
        lastTeleportTime = Time.unscaledTime;
    }

    private void CheckIfInsideObject()
    {
        if (playerCollider == null) return;

        Vector3 sphereCenter = transform.TransformPoint(playerCollider.center);
        float radius = playerCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);

        Collider[] overlaps = Physics.OverlapSphere(sphereCenter, radius, collisionMask);
        foreach (Collider col in overlaps)
        {
            if (col != playerCollider && col.gameObject != gameObject && !col.isTrigger)
            {
                isInsideObject = true; return;
            }
        }

        bool originalHitBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;

        Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        int insideVotes = 0, validRays = 0;

        try
        {
            foreach (Vector3 dir in directions)
            {
                int hitCount = Physics.RaycastNonAlloc(sphereCenter, dir, hitBuffer, insideCheckRayDistance, collisionMask);
                float closestDist = float.MaxValue;
                RaycastHit closestHit = default;
                bool foundValidMeshHit = false;

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = hitBuffer[i];
                    if (hit.collider == playerCollider || hit.collider.gameObject == gameObject || hit.collider.isTrigger) continue;

                    MeshCollider meshCol = hit.collider as MeshCollider;
                    if (meshCol == null || meshCol.convex) continue;

                    if (hit.distance < closestDist)
                    {
                        closestDist = hit.distance; closestHit = hit; foundValidMeshHit = true;
                    }
                }

                if (foundValidMeshHit)
                {
                    validRays++;
                    if (Vector3.Dot(dir, closestHit.normal) > 0f) insideVotes++;
                }
            }
        }
        finally { Physics.queriesHitBackfaces = originalHitBackfaces; }

        if (validRays > 0 && insideVotes > (validRays / 2)) { isInsideObject = true; return; }
        isInsideObject = false;
    }

    public void PrepareForTeleportAttack()
    {
        lockInput = true;
        isLockedOn = false;
        currentLockedTarget = null;
        currentVelocity = Vector3.zero;
        disabledScripts.Clear();

        MonoBehaviour[] allScripts = GetComponentsInChildren<MonoBehaviour>();
        foreach (MonoBehaviour script in allScripts)
        {
            if (script != this && script.enabled)
            {
                script.enabled = false;
                disabledScripts.Add(script);
            }
        }
    }

    public void CancelTeleportLock()
    {
        lockInput = false;
        foreach (MonoBehaviour script in disabledScripts)
        {
            if (script != null) script.enabled = true;
        }
        disabledScripts.Clear();
    }

    public void TriggerTeleportFlash()
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(TeleportFlashRoutine());
    }

    private IEnumerator TeleportFlashRoutine()
    {
        isFlashActive = true;
        float elapsed = 0f;

        while (elapsed < teleportFlashDuration)
        {
            elapsed += DeltaTime;
            flashExposureOverride = Mathf.Lerp(1.5f, 0.5f, elapsed / teleportFlashDuration);
            yield return null;
        }

        flashExposureOverride = 0.5f;
        isFlashActive = false;
    }

    public void FaceTargetInstantly(Vector3 targetPos)
    {
        Vector3 dirToTarget = (targetPos - cameraTransform.position).normalized;
        if (dirToTarget != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(dirToTarget, Vector3.up);
            currentRotation = targetRotation;
            cameraTransform.rotation = currentRotation;
            transform.rotation = currentRotation;
            lockOnReferenceUp = Vector3.up;
        }
    }

    public void SetRotationInstantly(Quaternion rotation)
    {
        isLockedOn = false;
        currentLockedTarget = null;
        targetRotation = rotation;
        currentRotation = rotation;
        if (cameraTransform != null) cameraTransform.rotation = rotation;
        transform.rotation = rotation;
    }

    public void StartTeleportThrow(Vector3 throwDir, float v0, float vf, float yDist, float maxAngVel)
    {
        StartCoroutine(TeleportThrowRoutine(throwDir, v0, vf, yDist, maxAngVel));
    }

    private IEnumerator TeleportThrowRoutine(Vector3 throwDir, float v0, float vf, float yDist, float maxAngVel)
    {
        float duration = (2f * yDist) / (v0 + vf);
        float elapsed = 0f;

        Vector3 currentAxis = Random.onUnitSphere;
        Vector3 targetAxis = Random.onUnitSphere;
        float axisChangeTimer = 0f;
        float axisChangeInterval = 0.25f;

        while (elapsed < duration)
        {
            elapsed += DeltaTime;
            float t = elapsed / duration;

            float currentSpeed = Mathf.Lerp(v0, vf, t);
            currentVelocity = throwDir * currentSpeed;

            float currentAngVel = Mathf.Lerp(maxAngVel, 0f, t);

            axisChangeTimer += DeltaTime;

            if (axisChangeTimer >= axisChangeInterval)
            {
                axisChangeTimer = 0f;
                targetAxis = Random.onUnitSphere;
                axisChangeInterval = Random.Range(0.15f, 0.4f);
            }

            currentAxis = Vector3.Slerp(currentAxis, targetAxis, DeltaTime * 5f).normalized;

            Quaternion deltaRot = Quaternion.AngleAxis(currentAngVel * DeltaTime, currentAxis);

            currentRotation = deltaRot * currentRotation;
            targetRotation = currentRotation;

            cameraTransform.rotation = currentRotation;
            transform.rotation = currentRotation;

            yield return null;
        }

        currentVelocity = throwDir * vf;

        lockInput = false;

        foreach (MonoBehaviour script in disabledScripts)
        {
            if (script != null)
                script.enabled = true;
        }
        disabledScripts.Clear();
    }

    public Vector3 GetCurrentVelocity() => currentVelocity;

    public void SetVelocity(Vector3 newVelocity) => currentVelocity = newVelocity;

    public void SetVelocityMagnitude(float magnitude)
    {
        if (currentVelocity.magnitude > 0.01f) currentVelocity = currentVelocity.normalized * magnitude;
        else currentVelocity = Vector3.zero;
    }
}