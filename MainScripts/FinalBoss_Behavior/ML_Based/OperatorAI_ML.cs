using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using System;
using System.Collections;
using System.Collections.Generic;

// Attack indices used by CanUse / TryUse / CooldownFraction / CostOf:
//   0 = Chase (no attack)   1 = Sword Straight   2 = Sword Round   3 = S_Pro
//   4 = Far Ring            5 = Teleport         6 = Ring          7 = Bomb (1)    8 = Bomb (3)
public class OperatorAI_ML : MonoBehaviour
{
    public const int ActionCount = 9;

    // Fix #1: Defaulted to true so ML mode is active by default
    public bool mlControlled = true;

    public bool phase1 = true;
    public bool phase1_5 = false;
    public bool phase2 = false;
    public bool phase3 = false;

    public GameObject phase1_5Particle;
    public GameObject phase2Particle;
    public Transform player;
    public LookAt childLookAtScript;
    public MirageSpell mirageSpell;
    public Transform anchor;
    public float maxAnchorDistance = 40000f;

    public float catchRange = 5f;
    public float acceleration = 150f;
    public float maxSpeed = 2000f;
    public float rotationSpeed = 8f;

    public float highRelativeSpeedThreshold = 800f;
    public float heavyAttackRange = 300f;

    public ManaSystem_Operator manaSystem;
    public float heavyAttackCooldown = 2.5f;
    public float heavyAttackCost = 8f;

    [Header("Tendrils")]
    [Tooltip("OperatorTendrils on this object (auto-found in Start if empty). Blocked for the whole Teleport attack so a caught player doesn't take two attacks at once.")]
    public OperatorTendrils operatorTendrils;
    [Tooltip("Seconds after the player is thrown (or after the Teleport ends / is cancelled) before the tendrils may attack again")]
    public float tendrilReenableDelay = 0.5f;

    [Header("S_Pro Attack Settings")]
    public List<GameObject> sProObjects = new List<GameObject>();
    public float sProCooldown = 10f;
    public float sProManaCost = 30f;
    public int sProActiveFrames = 360;

    [Header("Ring Attack")]
    public GameObject ringAttackPrefab;
    public float ringAttackCooldown = 8f;
    public float ringAttackManaCost = 80f;
    public float ringSpawnCollisionRadius = 10f;
    public float ringAttackDelay = 0f;
    public GameObject ringAttackTelegraphObject;

    [Header("Sword Attack Settings")]
    public float swordAttackRange = 10000f;
    [Tooltip("Sword Straight can only be used while the relative speed between the player and the operator is BELOW this value.")]
    public float swordStraightMaxRelativeSpeed = 2000f;
    public GameObject swordStraightObject;
    public GameObject swordRoundObject;
    public float swordDelay = 1f;
    public float swordFullDelay = 2.5f;
    public float swordRoundImmobileFrames = 120f; // Exact frame freeze for Sword Round
    public float swordStraightCooldown = 5f;
    public float swordRoundCooldown = 5f;
    public float swordManaCost = 0f;

    public float zone1Distance = 8000f;
    public float zone2Distance = 4000f;
    public float zone3Distance = 2000f;
    public float directFollowDistance = 1000f;
    public float wanderUpdateMin = 0.5f;
    public float wanderUpdateMax = 2f;
    public float wanderReachThreshold = 300f;

    public float phase1_5HaltDuration = 2f;
    public GameObject phase2Object1;
    public GameObject phase2Object2;
    public float phase2Period = 5f;
    public float phase3HaltDuration = 2f;

    public float velocityLockFadeDuration = 0.5f;

    [Header("Attack Ranges (masking)")]
    public float farRange = 30000f;
    public float veryFarRange = 60000f;
    [Tooltip("Energy Bombs can only be used while the player is further than this")]
    public float bombMinDistance = 5000f;
    [Tooltip("Ring can only be used while the player is further than this")]
    public float ringAttackMinDistance = 1500f;

    [Header("Far Ring")]
    public GameObject farRingObject;
    [Tooltip("Mana spent each time the Far Ring is cast")]
    public float farRingManaCost = 20f;
    [Tooltip("After a Far Ring, the Operator is frozen in place (no movement at all) and can't start any attack for this long")]
    public float farRingLockoutDelay = 5f;

    [Header("Energy Bomb (1)")]
    public GameObject bomb1Object;
    [Tooltip("Seconds the Operator stays stopped before control is given back")]
    public float bomb1Wait = 2f;
    public float bomb1ManaCost = 40f;
    [Tooltip("Seconds before Bomb (1) can be used again. Starts counting when it is cast, so it includes bomb1Wait.")]
    public float bomb1Cooldown = 8f;

    [Header("Energy Bomb (3)")]
    public GameObject bomb3Object;
    [Tooltip("Seconds the Operator stays stopped before control is given back")]
    public float bomb3Wait = 3f;
    public float bomb3ManaCost = 80f;
    [Tooltip("Seconds before Bomb (3) can be used again. Starts counting when it is cast, so it includes bomb3Wait.")]
    public float bomb3Cooldown = 12f;

    [Header("Teleport Attack")]
    public Transform operatorBodySlot;
    public float teleportAttackCooldown = 6f;
    public float teleportAttackCost = 15f;
    public float teleportDelay1 = 1f;
    public GameObject teleportObject1;
    public GameObject preFlashObject;
    public float teleportLookDuration = 0.2f;
    public float teleportDistanceX = 1500f;
    public float teleportDelay2 = 1.5f;
    public GameObject teleportObject2;
    public float teleportPullPercent = 50f;
    public float teleportThrowMaxVelocity = 25000f;
    public float teleportThrowDistanceY = 5000f;
    public float teleportThrowMaxAngularVelocity = 720f;

    [Header("Flash Settings")]
    public Volume globalVolume;
    public Material flashMaterial;
    public Material teleportSkyboxMaterial;

    [Header("Training Dodge Simulation")]
    public bool simulatePlayerDodge = false;
    [Range(0f, 1f)] public float simulatedDodgeChance = 0.6f;
    public float simulatedReactionMin = 0.25f;
    public float simulatedReactionMax = 0.4f;

    private float teleportAttackTimer;
    private bool isPerformingTeleportAttack = false;
    private bool isInTeleportAttackDelay = false;
    private Coroutine teleportAttackCoroutine = null;
    private float savedTimeScale = 1f;
    private float farRingLockTimer = 0f;

    private bool isPerformingBomb = false;
    private Coroutine bombCoroutine = null;
    private float bomb1Timer = 0f;
    private float bomb3Timer = 0f;

    private Material _originalSkyboxMaterial;
    private readonly Color defaultFilterColor = new Color(207f / 255f, 207f / 255f, 207f / 255f);
    private readonly Color activeFilterColor = new Color(100f / 255f, 202f / 255f, 105f / 255f);
    private Dictionary<Renderer, Material[]> _originalMaterials = new();
    private List<MonoBehaviour> _disabledScripts = new List<MonoBehaviour>();

    public Vector3 PlayerVelocity { get; private set; }

    private bool isPhaseTransitioning = false;
    private bool isPhase2Waiting = false;
    private Coroutine phaseTransitionCoroutine = null;
    private Coroutine phase2Coroutine = null;

    private float sProTimer = 0f;
    private bool isPerformingSPro = false;
    private Coroutine sProCoroutine = null;
    private List<GameObject> activeSProObjects = new List<GameObject>();
    private int activeSProRoutines = 0;
    private List<Coroutine> sProSubCoroutines = new List<Coroutine>();

    private float ringAttackTimer = 0f;
    private bool isPerformingRingAttack = false;
    private bool isInRingAttackDelay = false;
    private Coroutine ringAttackCoroutine = null;

    private float swordStraightTimer = 0f;
    private float swordRoundTimer = 0f;
    private bool isPerformingSwordAttack = false;
    private bool isSwordImmobile = false;
    private Coroutine swordCoroutine = null;
    private GameObject activeSwordObject = null;

    private Vector3 wanderTarget;
    private float wanderTimer = 0f;
    private Rigidbody rb;
    private OperatorHealth operatorHealth;
    private Vector3 currentVelocity;
    private Vector3 lastPlayerPosition;
    private float heavyAttackTimer;
    private Transform currentTarget;

    private bool isPushed = false;
    private float pushTimer = 0f;
    private float pushDuration = 1f;
    private float pushMaxSpeed = 4000f;
    private float pushCriticalSpeed = 500f;
    private Vector3 pushDirection = Vector3.zero;

    private bool isVelocityLocked = false;
    private float velocityLockFadeTimer = 0f;
    private Vector3 velocityLockStartVelocity;
    private Vector3 velocityLockTargetVelocity;

    public bool HasCaughtPlayer { get; private set; }
    public enum OperatorState { Chasing, AnchorPull }
    public OperatorState CurrentState { get; private set; }

    // ML Tracker Hooks
    public event Action<float> OnPlayerDamaged;
    private List<GameObject> activeProjectiles = new List<GameObject>();

    public void NotifyPlayerDamaged(float amount) => OnPlayerDamaged?.Invoke(amount);

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        operatorHealth = GetComponent<OperatorHealth>();

        if (operatorTendrils == null)
            operatorTendrils = GetComponent<OperatorTendrils>();

        if (rb != null)
        {
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.constraints = RigidbodyConstraints.None;
        }

        if (player != null)
        {
            lastPlayerPosition = player.position;
            wanderTarget = player.position;
            RefreshWanderTarget();
        }

        currentTarget = player;
    }

    void Update()
    {
        if (player == null) return;

        sProTimer -= Time.deltaTime;
        heavyAttackTimer -= Time.deltaTime;
        teleportAttackTimer -= Time.deltaTime;
        farRingLockTimer -= Time.deltaTime;
        swordStraightTimer -= Time.deltaTime;
        swordRoundTimer -= Time.deltaTime;
        ringAttackTimer -= Time.deltaTime;
        bomb1Timer -= Time.deltaTime;
        bomb3Timer -= Time.deltaTime;

        RefreshTarget();

        if (currentTarget == player)
        {
            wanderTimer -= Time.deltaTime;
            if (wanderTimer <= 0f) RefreshWanderTarget();
        }
    }

    void FixedUpdate()
    {
        if (currentTarget == null) return;

        CheckHealthPhases();

        PlayerVelocity = (player.position - lastPlayerPosition) / Time.fixedDeltaTime;
        lastPlayerPosition = player.position;

        if (IsMovementBlocked())
        {
            currentVelocity = Vector3.zero;
            if (rb != null) rb.linearVelocity = Vector3.zero;
            return;
        }

        Vector3 chasePos = GetChasePosition();
        float distToChase = Vector3.Distance(transform.position, chasePos);
        float distToPlayer = Vector3.Distance(transform.position, player.position);
        float distanceToAnchor = anchor != null ? Vector3.Distance(transform.position, anchor.position) : 0f;

        if (currentTarget == player && distToChase < wanderReachThreshold)
            RefreshWanderTarget();

        if (!isVelocityLocked)
        {
            UpdateState(distToChase, distToPlayer, distanceToAnchor);
            HandleRotation(chasePos);
        }

        HandleMovement(chasePos, distToChase, distanceToAnchor);
    }

    // --- ML Agent Hooks ---

    public Vector3 GetCurrentVelocity() => currentVelocity;

    public void SyncPlayer()
    {
        lastPlayerPosition = player.position;
        PlayerVelocity = Vector3.zero;
    }

    public float RelativePlayerSpeed => (PlayerVelocity - currentVelocity).magnitude;

    public float GetManaFraction() => (manaSystem != null && manaSystem.maxMana > 0f) ? manaSystem.CurrentMana / manaSystem.maxMana : 1f;

    public float CooldownFraction(int i)
    {
        float t = i switch
        {
            1 => swordStraightTimer,
            2 => swordRoundTimer,
            3 => sProTimer,
            4 => farRingLockTimer,
            5 => teleportAttackTimer,
            6 => ringAttackTimer,
            7 => bomb1Timer,
            8 => bomb3Timer,
            _ => 0f
        };
        float total = i switch
        {
            1 => swordStraightCooldown,
            2 => swordRoundCooldown,
            3 => sProCooldown,
            4 => farRingLockoutDelay,
            5 => teleportAttackCooldown,
            6 => ringAttackCooldown,
            7 => bomb1Cooldown,
            8 => bomb3Cooldown,
            _ => 1f
        };
        return total > 0f ? Mathf.Clamp01(t / total) : 0f;
    }

    public float MaxMana => (manaSystem != null && manaSystem.maxMana > 0f) ? manaSystem.maxMana : 1f;

    public float CostOf(int i) => i switch
    {
        1 or 2 => swordManaCost,
        3 => sProManaCost,
        4 => farRingManaCost,
        5 => teleportAttackCost,
        6 => ringAttackManaCost,
        7 => bomb1ManaCost,
        8 => bomb3ManaCost,
        _ => 0f
    };

    public bool IsAttackBlocked() => isPerformingSPro || isPerformingRingAttack || isPerformingTeleportAttack || isPerformingBomb || isPhaseTransitioning || phase2 || isPerformingSwordAttack;

    private bool IsMovementBlocked() => isPerformingSPro || isPerformingRingAttack || isPerformingTeleportAttack || isPerformingBomb || isPhaseTransitioning || phase2 || isSwordImmobile || farRingLockTimer > 0f;

    public bool CanUse(int i)
    {
        if (i == 0) return true;
        if (!(phase1 || phase1_5 || phase3) || IsAttackBlocked() || farRingLockTimer > 0f) return false;

        float d = Vector3.Distance(transform.position, player.position);
        bool mana = manaSystem == null || manaSystem.CurrentMana >= CostOf(i);

        return i switch
        {
            1 => swordStraightTimer <= 0f && mana && d <= swordAttackRange && RelativePlayerSpeed < swordStraightMaxRelativeSpeed,
            2 => swordRoundTimer <= 0f && mana && d <= swordAttackRange,
            3 => sProTimer <= 0f && mana && d > swordAttackRange && d < farRange,
            4 => farRingObject != null && mana && d > farRange && d < veryFarRange,
            5 => teleportAttackTimer <= 0f && mana && d > farRange,
            6 => ringAttackPrefab != null && ringAttackTimer <= 0f && mana && d > ringAttackMinDistance,
            7 => bomb1Object != null && bomb1Timer <= 0f && mana && d > bombMinDistance,
            8 => bomb3Object != null && bomb3Timer <= 0f && mana && d > bombMinDistance,
            _ => false
        };
    }

    public void TryUse(int i)
    {
        switch (i)
        {
            case 1:
                if (manaSystem == null || manaSystem.UseMana(swordManaCost))
                {
                    swordStraightTimer = swordStraightCooldown;
                    swordCoroutine = StartCoroutine(SwordStraightRoutine());
                }
                break;

            case 2:
                if (manaSystem == null || manaSystem.UseMana(swordManaCost))
                {
                    swordRoundTimer = swordRoundCooldown;
                    swordCoroutine = StartCoroutine(SwordRoundFrameRoutine());
                }
                break;

            case 3:
                sProTimer = sProCooldown;
                sProCoroutine = StartCoroutine(SProAttackRoutine());
                break;

            case 4:
                if (manaSystem != null && !manaSystem.UseMana(farRingManaCost)) break;

                farRingObject.SetActive(false);
                farRingObject.SetActive(true);
                farRingLockTimer = farRingLockoutDelay;

                // Freeze immediately; IsMovementBlocked() keeps it frozen for the whole lockout
                currentVelocity = Vector3.zero;
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                break;

            case 5:
                if (manaSystem == null || manaSystem.UseMana(teleportAttackCost))
                {
                    teleportAttackTimer = teleportAttackCooldown;
                    teleportAttackCoroutine = StartCoroutine(TeleportAttackRoutine());
                }
                break;

            case 6:
                if (manaSystem == null || manaSystem.UseMana(ringAttackManaCost))
                {
                    ringAttackTimer = ringAttackCooldown;
                    ringAttackCoroutine = StartCoroutine(RingAttackRoutine());
                }
                break;

            case 7:
                if (manaSystem == null || manaSystem.UseMana(bomb1ManaCost))
                {
                    bomb1Timer = bomb1Cooldown;
                    bombCoroutine = StartCoroutine(BombRoutine(bomb1Object, bomb1Wait));
                }
                break;

            case 8:
                if (manaSystem == null || manaSystem.UseMana(bomb3ManaCost))
                {
                    bomb3Timer = bomb3Cooldown;
                    bombCoroutine = StartCoroutine(BombRoutine(bomb3Object, bomb3Wait));
                }
                break;
        }
    }

    // Scripted trigger (bypasses cooldown / mana). The agent uses TryUse(6) instead.
    public void TriggerRingAttack()
    {
        if (IsAttackBlocked()) return;
        ringAttackCoroutine = StartCoroutine(RingAttackRoutine());
    }

    public void ResetForEpisode(Vector3 startPos)
    {
        CancelAttack();

        currentVelocity = Vector3.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.position = startPos;

        phase1 = true;
        phase1_5 = phase2 = phase3 = false;
        isPushed = isVelocityLocked = false;
        if (childLookAtScript != null) childLookAtScript.enabled = true;
        foreach (GameObject g in new[] { phase1_5Particle, phase2Particle, phase2Object1, phase2Object2 })
            if (g != null) g.SetActive(false);

        sProTimer = heavyAttackTimer = swordStraightTimer = swordRoundTimer = teleportAttackTimer = farRingLockTimer = 0f;
        ringAttackTimer = bomb1Timer = bomb3Timer = 0f;
        PlayerVelocity = Vector3.zero;
        if (farRingObject != null) farRingObject.SetActive(false);
        if (bomb1Object != null) bomb1Object.SetActive(false);
        if (bomb3Object != null) bomb3Object.SetActive(false);

        if (operatorHealth != null) operatorHealth.RestoreToMax();
        if (manaSystem != null) manaSystem.RestoreToMax();

        foreach (var proj in activeProjectiles)
        {
            if (proj != null) Destroy(proj);
        }
        activeProjectiles.Clear();
    }

    IEnumerator SProAttackRoutine()
    {
        isPerformingSPro = true;
        activeSProObjects.Clear();
        sProSubCoroutines.Clear();
        activeSProRoutines = 0;

        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        int fireCount = UnityEngine.Random.Range(1, 5);
        for (int i = 0; i < fireCount; i++)
        {
            if (manaSystem != null && !manaSystem.UseMana(sProManaCost)) break;
            if (sProObjects != null && sProObjects.Count > 0)
            {
                GameObject chosenObj = sProObjects[UnityEngine.Random.Range(0, sProObjects.Count)];
                if (chosenObj != null)
                {
                    chosenObj.SetActive(true);
                    if (!activeSProObjects.Contains(chosenObj)) activeSProObjects.Add(chosenObj);
                    sProSubCoroutines.Add(StartCoroutine(DisableObjectAfterFrames(chosenObj, sProActiveFrames)));
                }
            }
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.2f, 0.5f));
        }

        while (activeSProRoutines > 0) yield return null;
        activeSProObjects.Clear();
        sProSubCoroutines.Clear();
        isPerformingSPro = false;
        sProCoroutine = null;
    }

    IEnumerator DisableObjectAfterFrames(GameObject obj, int frames)
    {
        activeSProRoutines++;
        for (int i = 0; i < frames; i++) yield return new WaitForFixedUpdate();
        if (obj != null) obj.SetActive(false);
        activeSProRoutines--;
    }

    IEnumerator RingAttackRoutine()
    {
        isPerformingRingAttack = true;
        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (ringAttackTelegraphObject != null) ringAttackTelegraphObject.SetActive(true);

        isInRingAttackDelay = true;
        if (ringAttackDelay > 0f) yield return new WaitForSeconds(ringAttackDelay);
        isInRingAttackDelay = false;

        if (ringAttackPrefab != null)
        {
            Vector3 spawnPosition = transform.position + (player.position - transform.position) * 0.98f;
            GameObject spawnedRing = Instantiate(ringAttackPrefab, spawnPosition, transform.rotation);
            activeProjectiles.Add(spawnedRing);

            if (Physics.CheckSphere(spawnPosition, ringSpawnCollisionRadius, LayerMask.GetMask("SceneObject")))
                Destroy(spawnedRing);
        }

        if (ringAttackTelegraphObject != null) ringAttackTelegraphObject.SetActive(false);
        isPerformingRingAttack = false;
        ringAttackCoroutine = null;
    }

    IEnumerator SwordStraightRoutine()
    {
        isPerformingSwordAttack = true;
        isSwordImmobile = true;
        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        activeSwordObject = swordStraightObject;
        if (activeSwordObject != null) activeSwordObject.SetActive(true);

        if (swordDelay > 0f) yield return new WaitForSeconds(swordDelay);
        isSwordImmobile = false;

        float remainingDelay = Mathf.Max(0f, swordFullDelay - swordDelay);
        if (remainingDelay > 0f) yield return new WaitForSeconds(remainingDelay);

        if (activeSwordObject != null) activeSwordObject.SetActive(false);
        activeSwordObject = null;
        isPerformingSwordAttack = false;
        swordCoroutine = null;
    }

    IEnumerator SwordRoundFrameRoutine()
    {
        isPerformingSwordAttack = true;
        isSwordImmobile = true;
        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        activeSwordObject = swordRoundObject;
        if (activeSwordObject != null) activeSwordObject.SetActive(true);

        int totalFrames = Mathf.Max(1, Mathf.RoundToInt(swordRoundImmobileFrames));
        for (int i = 0; i < totalFrames; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        if (activeSwordObject != null) activeSwordObject.SetActive(false);
        activeSwordObject = null;
        isSwordImmobile = false;
        isPerformingSwordAttack = false;
        swordCoroutine = null;
    }

    IEnumerator BombRoutine(GameObject bombObject, float wait)
    {
        isPerformingBomb = true;

        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        if (bombObject != null)
        {
            bombObject.SetActive(false);
            bombObject.SetActive(true);
        }

        if (wait > 0f) yield return new WaitForSeconds(wait);

        isPerformingBomb = false;
        bombCoroutine = null;
    }

    // Lets the tendrils attack again tendrilReenableDelay seconds from now.
    void ReleaseTendrilsAfterTeleport()
    {
        if (operatorTendrils != null)
            operatorTendrils.UnblockAfter(tendrilReenableDelay);
    }

    IEnumerator TeleportAttackRoutine()
    {
        isPerformingTeleportAttack = true;

        // The tendrils stay off for the whole teleport so a caught player doesn't eat two attacks at once.
        // (Also stops a tendril attack that is already mid-expansion.)
        if (operatorTendrils != null) operatorTendrils.BlockAttacks();

        currentVelocity = Vector3.zero;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        BasicMovement_2 playerMovement = player.GetComponent<BasicMovement_2>();

        if (teleportObject1 != null) teleportObject1.SetActive(true);
        isInTeleportAttackDelay = true;

        if (teleportDelay1 > 0f) yield return new WaitForSeconds(teleportDelay1);

        isInTeleportAttackDelay = false;
        if (teleportObject1 != null) teleportObject1.SetActive(false);

        if (playerMovement != null) playerMovement.PrepareForTeleportAttack();

        if (preFlashObject != null) preFlashObject.SetActive(true);

        _disabledScripts.Clear();
        foreach (MonoBehaviour mb in GetComponents<MonoBehaviour>())
        {
            if (mb == this || !mb.enabled) continue;
            mb.enabled = false;
            _disabledScripts.Add(mb);
        }

        SetColorFilter(activeFilterColor);

        _originalSkyboxMaterial = RenderSettings.skybox;
        if (teleportSkyboxMaterial != null)
        {
            RenderSettings.skybox = teleportSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        FlashMaterials();

        Transform bodyTarget = operatorBodySlot != null ? operatorBodySlot : transform;
        Vector3 teleportDir = playerMovement != null ? playerMovement.transform.forward : player.forward;
        Vector3 startPos = transform.position;
        Vector3 endPos = player.position + teleportDir * teleportDistanceX;

        float totalDistance = Vector3.Distance(startPos, endPos);
        Vector3 moveDir = (endPos - startPos).normalized;

        float elapsedTime = 0f;
        float currentDistance = 0f;
        bool playerDodged = false;
        bool simWillDodge = simulatePlayerDodge && UnityEngine.Random.value < simulatedDodgeChance;
        float simReaction = UnityEngine.Random.Range(simulatedReactionMin, simulatedReactionMax);

        while (currentDistance < totalDistance)
        {
            elapsedTime += Time.unscaledDeltaTime;

            if (!playerDodged && ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) || (simWillDodge && elapsedTime >= simReaction)))
            {
                playerDodged = true;

                if (playerMovement != null)
                {
                    player.position -= playerMovement.transform.forward * playerMovement.teleportDistance;
                    playerMovement.CancelTeleportLock();
                }

                foreach (MonoBehaviour mb in _disabledScripts)
                {
                    if (mb != null) mb.enabled = true;
                }
                _disabledScripts.Clear();

                RestoreMaterials();
                Time.timeScale = savedTimeScale;
                SetColorFilter(defaultFilterColor);

                if (_originalSkyboxMaterial != null)
                {
                    RenderSettings.skybox = _originalSkyboxMaterial;
                    DynamicGI.UpdateEnvironment();
                }

                if (preFlashObject != null) preFlashObject.SetActive(false);
            }

            currentDistance = 15000f * (Mathf.Exp(2f * elapsedTime) - 1f);
            float clampedDistance = Mathf.Min(currentDistance, totalDistance);
            transform.position = startPos + moveDir * clampedDistance;

            if (!playerDodged)
            {
                Vector3 dirToOperator = (bodyTarget.position - player.position).normalized;
                if (dirToOperator != Vector3.zero)
                {
                    Quaternion targetPlayerRot = Quaternion.LookRotation(dirToOperator, Vector3.up);
                    player.rotation = Quaternion.RotateTowards(player.rotation, targetPlayerRot, 180f * Time.unscaledDeltaTime);
                }

                if (playerMovement != null)
                {
                    playerMovement.lockInput = true;
                    playerMovement.SetVelocity(Vector3.zero);
                }
            }

            yield return null;
        }
        transform.position = endPos;

        if (playerDodged)
        {
            isPerformingTeleportAttack = false;
            teleportAttackCoroutine = null;
            ReleaseTendrilsAfterTeleport();
            yield break;
        }

        RestoreMaterials();
        Time.timeScale = savedTimeScale;
        SetColorFilter(defaultFilterColor);

        if (_originalSkyboxMaterial != null)
        {
            RenderSettings.skybox = _originalSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        if (playerMovement != null)
        {
            var type = typeof(BasicMovement_2);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            Quaternion newRot = player.rotation;
            type.GetField("targetRotation", flags)?.SetValue(playerMovement, newRot);
            type.GetField("currentRotation", flags)?.SetValue(playerMovement, newRot);
        }

        foreach (MonoBehaviour mb in _disabledScripts)
        {
            if (mb != null) mb.enabled = true;
        }
        _disabledScripts.Clear();

        if (preFlashObject != null) preFlashObject.SetActive(false);

        if (playerMovement != null) playerMovement.TriggerTeleportFlash();

        if (teleportObject2 != null) teleportObject2.SetActive(true);

        float pullDistance = teleportDistanceX * (teleportPullPercent / 100f);
        Vector3 startPlayerPos = player.position;
        Vector3 endPlayerPos = player.position + (bodyTarget.position - player.position).normalized * pullDistance;

        float elapsed = 0f;
        while (elapsed < teleportDelay2)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / teleportDelay2;

            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            player.position = Vector3.Lerp(startPlayerPos, endPlayerPos, smoothT);

            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            if (directionToPlayer != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(directionToPlayer, transform.up);
            }

            if (playerMovement != null)
            {
                playerMovement.FaceTargetInstantly(bodyTarget.position);
                playerMovement.lockInput = true;
                playerMovement.SetVelocity(Vector3.zero);
            }

            yield return null;
        }

        player.position = endPlayerPos;
        if (teleportObject2 != null) teleportObject2.SetActive(false);

        if (playerMovement != null)
        {
            Vector3 throwDir = (player.position - bodyTarget.position).normalized;
            playerMovement.StartTeleportThrow(throwDir, teleportThrowMaxVelocity, playerMovement.maxSafeSpeed, teleportThrowDistanceY, teleportThrowMaxAngularVelocity);
        }

        isPerformingTeleportAttack = false;
        teleportAttackCoroutine = null;

        // Tendrils come back tendrilReenableDelay after the teleport finishes
        ReleaseTendrilsAfterTeleport();
    }

    void FlashMaterials()
    {
        if (flashMaterial == null) return;

        _originalMaterials.Clear();

        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (Renderer rend in allRenderers)
        {
            _originalMaterials[rend] = rend.sharedMaterials;

            Material[] swapped = new Material[rend.sharedMaterials.Length];
            for (int i = 0; i < swapped.Length; i++)
                swapped[i] = flashMaterial;

            rend.sharedMaterials = swapped;
        }
    }

    void RestoreMaterials()
    {
        foreach (var kvp in _originalMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.sharedMaterials = kvp.Value;
        }

        _originalMaterials.Clear();
    }

    void SetColorFilter(Color color)
    {
        if (globalVolume == null) return;

        if (globalVolume.profile.TryGet<ColorAdjustments>(out ColorAdjustments ca))
            ca.colorFilter.Override(color);
    }

    void TryHeavyAttack()
    {
        if (heavyAttackTimer > 0f) return;
        if (manaSystem == null || manaSystem.CurrentMana >= heavyAttackCost)
        {
            heavyAttackTimer = heavyAttackCooldown;
            if (manaSystem == null || manaSystem.UseMana(heavyAttackCost)) { }
        }
    }

    void RefreshWanderTarget()
    {
        if (player == null) return;
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= directFollowDistance) wanderTarget = player.position;
        else
        {
            float r = dist > zone1Distance ? zone1Distance : (dist > zone2Distance ? zone2Distance : zone3Distance);
            wanderTarget = player.position + UnityEngine.Random.insideUnitSphere * r;
        }
        wanderTimer = UnityEngine.Random.Range(wanderUpdateMin, wanderUpdateMax);
    }

    Vector3 GetChasePosition() => currentTarget != player ? currentTarget.position : wanderTarget;

    void RefreshTarget()
    {
        if (mirageSpell == null || mirageSpell.GetActiveMirageCount() == 0) { currentTarget = player; return; }
        Transform closest = null;
        float closestDist = float.MaxValue;
        foreach (GameObject mirage in mirageSpell.ActiveMirages)
        {
            if (mirage == null) continue;
            float d = Vector3.Distance(transform.position, mirage.transform.position);
            if (d < closestDist) { closestDist = d; closest = mirage.transform; }
        }
        currentTarget = closest != null ? closest : player;
    }

    void UpdateState(float distToChase, float distToPlayer, float distanceToAnchor)
    {
        HasCaughtPlayer = (distToPlayer <= catchRange && currentTarget == player);
        CurrentState = distanceToAnchor > maxAnchorDistance ? OperatorState.AnchorPull : OperatorState.Chasing;
        if (CurrentState == OperatorState.AnchorPull) HasCaughtPlayer = false;
    }

    void HandleRotation(Vector3 chasePos)
    {
        Vector3 lookAt = CurrentState == OperatorState.AnchorPull && anchor != null ? anchor.position : chasePos;
        Vector3 direction = (lookAt - transform.position).normalized;
        if (direction == Vector3.zero) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, transform.up), rotationSpeed * Time.fixedDeltaTime);
    }

    void HandleMovement(Vector3 chasePos, float distToChase, float distanceToAnchor)
    {
        if (isVelocityLocked)
        {
            velocityLockFadeTimer += Time.fixedDeltaTime;
            currentVelocity = Vector3.Lerp(velocityLockStartVelocity, velocityLockTargetVelocity, velocityLockFadeDuration > 0f ? Mathf.Clamp01(velocityLockFadeTimer / velocityLockFadeDuration) : 1f);
            if (rb != null) rb.linearVelocity = currentVelocity; else transform.position += currentVelocity * Time.fixedDeltaTime;
            return;
        }

        if (isPushed)
        {
            pushTimer -= Time.fixedDeltaTime;
            if (pushTimer <= 0f) { isPushed = false; currentVelocity = pushDirection * pushCriticalSpeed; }
            else currentVelocity = pushDirection * Mathf.Lerp(pushMaxSpeed, pushCriticalSpeed, 1f - (pushTimer / pushDuration));
            if (rb != null) rb.linearVelocity = currentVelocity; else transform.position += currentVelocity * Time.fixedDeltaTime;
            return;
        }

        switch (CurrentState)
        {
            case OperatorState.AnchorPull:
                currentVelocity = Vector3.ClampMagnitude(currentVelocity + (anchor.position - transform.position).normalized * (acceleration * 4f) * Time.fixedDeltaTime, maxSpeed * 4f);
                break;
            case OperatorState.Chasing:
                currentVelocity = Vector3.ClampMagnitude(currentVelocity + (chasePos - transform.position).normalized * acceleration * Time.fixedDeltaTime, maxSpeed);
                break;
        }

        if (rb != null) rb.linearVelocity = currentVelocity; else transform.position += currentVelocity * Time.fixedDeltaTime;
    }

    void CheckHealthPhases()
    {
        if (operatorHealth == null) return;
        float healthPct = operatorHealth.CurrentHealth / operatorHealth.MaxHealth;
        if (healthPct <= 0.3333f && !phase3)
        {
            phase1 = phase1_5 = phase2 = false; phase3 = true;
            if (childLookAtScript != null) childLookAtScript.enabled = true;
            CancelAttack();
            if (phaseTransitionCoroutine != null) StopCoroutine(phaseTransitionCoroutine);
            phaseTransitionCoroutine = StartCoroutine(PhaseTransitionRoutine(phase3HaltDuration));
        }
        else if (healthPct <= 0.67f && !phase2 && !phase3)
        {
            phase1 = phase1_5 = false; phase2 = true;
            if (childLookAtScript != null) childLookAtScript.enabled = false;
            CancelAttack();
            if (phase2Particle != null) phase2Particle.SetActive(true);
            phase2Coroutine = StartCoroutine(Phase2Routine());
        }
        else if (healthPct <= 0.80f && !phase1_5 && !phase2 && !phase3)
        {
            phase1 = false; phase1_5 = true;
            if (childLookAtScript != null) childLookAtScript.enabled = true;
            CancelAttack();
            if (phase1_5Particle != null) phase1_5Particle.SetActive(true);
            if (phaseTransitionCoroutine != null) StopCoroutine(phaseTransitionCoroutine);
            phaseTransitionCoroutine = StartCoroutine(PhaseTransitionRoutine(phase1_5HaltDuration));
        }
    }

    IEnumerator PhaseTransitionRoutine(float duration)
    {
        isPhaseTransitioning = true;
        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        yield return new WaitForSeconds(duration);
        isPhaseTransitioning = false;
        phaseTransitionCoroutine = null;
    }

    IEnumerator Phase2Routine()
    {
        isPhase2Waiting = true;
        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        if (phase2Object1 != null) phase2Object1.SetActive(true);
        yield return new WaitForSeconds(phase2Period);
        if (!phase3 && phase2Object2 != null) phase2Object2.SetActive(true);
        isPhase2Waiting = false;
        phase2Coroutine = null;
    }

    public void CancelAttack()
    {
        if (isPhaseTransitioning) { if (phaseTransitionCoroutine != null) StopCoroutine(phaseTransitionCoroutine); isPhaseTransitioning = false; }
        if (phase2Coroutine != null) { StopCoroutine(phase2Coroutine); phase2Coroutine = null; isPhase2Waiting = false; }

        if (isPerformingSwordAttack)
        {
            if (swordCoroutine != null) StopCoroutine(swordCoroutine);
            if (activeSwordObject != null) activeSwordObject.SetActive(false);
            if (swordStraightObject != null) swordStraightObject.SetActive(false);
            if (swordRoundObject != null) swordRoundObject.SetActive(false);
            isPerformingSwordAttack = isSwordImmobile = false;
        }

        if (isPerformingSPro)
        {
            if (sProCoroutine != null) StopCoroutine(sProCoroutine);
            foreach (Coroutine c in sProSubCoroutines) if (c != null) StopCoroutine(c);
            sProSubCoroutines.Clear(); activeSProRoutines = 0;
            foreach (GameObject obj in activeSProObjects) if (obj != null) obj.SetActive(false);
            activeSProObjects.Clear(); isPerformingSPro = false;
        }

        if (isPerformingRingAttack)
        {
            if (ringAttackCoroutine != null) StopCoroutine(ringAttackCoroutine);
            if (ringAttackTelegraphObject != null) ringAttackTelegraphObject.SetActive(false);
            isInRingAttackDelay = isPerformingRingAttack = false;
        }

        if (isPerformingBomb)
        {
            if (bombCoroutine != null) StopCoroutine(bombCoroutine);
            bombCoroutine = null;
            if (bomb1Object != null) bomb1Object.SetActive(false);
            if (bomb3Object != null) bomb3Object.SetActive(false);
            isPerformingBomb = false;
        }

        if (isInTeleportAttackDelay || isPerformingTeleportAttack)
        {
            if (teleportAttackCoroutine != null) StopCoroutine(teleportAttackCoroutine);
            teleportAttackCoroutine = null;

            if (teleportObject1 != null) teleportObject1.SetActive(false);
            if (teleportObject2 != null) teleportObject2.SetActive(false);
            if (preFlashObject != null) preFlashObject.SetActive(false);

            RestoreMaterials();
            SetColorFilter(defaultFilterColor);

            if (_originalSkyboxMaterial != null)
            {
                RenderSettings.skybox = _originalSkyboxMaterial;
                DynamicGI.UpdateEnvironment();
            }

            if (Time.timeScale == 0f) Time.timeScale = savedTimeScale;

            foreach (MonoBehaviour mb in _disabledScripts)
                if (mb != null) mb.enabled = true;
            _disabledScripts.Clear();

            isInTeleportAttackDelay = isPerformingTeleportAttack = false;

            // Teleport is over, so the tendrils may come back (after the usual delay)
            ReleaseTendrilsAfterTeleport();
        }
    }
}