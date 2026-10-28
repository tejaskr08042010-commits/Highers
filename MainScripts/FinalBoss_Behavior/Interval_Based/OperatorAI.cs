// =====================================================================================
//  ATTACK AVAILABILITY
//  Distance = Operator -> player. All ranges are set in the "Attack Ranges" header.
//
//  Attack           Available when
//  ---------------  ----------------------------------------------------------------
//  Sword Straight   distance <= swordAttackRange
//                   AND relative speed (player vs operator) < swordStraightMaxRelativeSpeed
//                   AND off cooldown
//  Sword Round      distance <= swordAttackRange AND off cooldown
//  S_Pro            swordAttackRange < distance < farRange AND off cooldown
//  Far Ring         farRange < distance < veryFarRange AND enough mana (farRingManaCost)
//                   (no cooldown; the Operator is FROZEN in place and makes no attack
//                    decisions for farRingLockoutDelay)
//  Teleport         distance > farRange AND off cooldown
//                   (FORCED over all other attacks when distance > veryFarRange)
//  Bomb (1), (3)    distance > bombMinDistance AND off cooldown AND enough mana
//                   (each bomb has its own cooldown: bomb1Cooldown / bomb3Cooldown)
//  Ring             distance > ringAttackMinDistance AND off cooldown (needs the prefab)
//
//  The ranges overlap on purpose. Each decision picks at random among the attacks that are
//  available AND affordable (mana) at that moment, so overlap = more variety.
//  An attack must also be enabled for the current phase (p1_ / p1_5_ / p3_ toggles).
//  If nothing can be cast, it re-checks every decisionRetryInterval seconds.
//
//  CHASE: the Operator is faster than the player and always tries (never forced) to be within
//  swordAttackRange: each move target is a random point in a sphere of radius
//  swordAttackRange * chaseRangeScale around the player. When the player is far it keeps
//  attacking normally and keeps pushing toward that sphere.
//
//  PANIC: while the Operator's mana is below panicMana it cannot start any attack and
//  chases at maxSpeed relative speed (no slowing down near the player) so it is hard to hit.
//
//  TENDRILS: OperatorTendrils (same object) is blocked for the whole Teleport attack and is
//  allowed to attack again tendrilReenableDelay seconds after the player is thrown.
//
//  COMPRESS: CancelEverything() is called by CompressSpell the moment the orb connects.
//
//  PHASE 2: has no timer. When Phase 2 starts, the Operator stops moving, the Phase 2 particle
//  plays and BOTH phase2Object1 and phase2Object2 are enabled at once. It lasts until Phase 3
//  begins (health-based). At that moment phase2Object1 and phase2Object2 are destroyed and
//  phase2FinalObject is enabled.
//
//  DEATH: the moment health hits 0 the Operator is stopped dead exactly like CompressSpell does
//  (CancelEverything) and stays frozen.
// =====================================================================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class OperatorAI : MonoBehaviour
{
    public enum AttackType { SwordStraight, SwordRound, SPro, Ring, FarRing, Teleport, Bomb1, Bomb3 }
    public enum OperatorState { Chasing, AnchorPull }

    // ---------------------------------------------------------------
    //  Phases
    // ---------------------------------------------------------------
    [Header("Phases (state)")]
    public bool phase1 = true;
    public bool phase1_5 = false;
    public bool phase2 = false;
    public bool phase3 = false;

    [Tooltip("GameObject with ParticleSystem enabled at start of Phase 1.5")]
    public GameObject phase1_5Particle;
    [Tooltip("GameObject with ParticleSystem enabled at start of Phase 2")]
    public GameObject phase2Particle;

    [Header("Phase Health Thresholds (% of max health)")]
    [Tooltip("Phase 1.5 starts when health drops to or below this percentage")]
    [Range(0f, 100f)] public float phase1_5HealthPercent = 80f;
    [Tooltip("Phase 2 starts when health drops to or below this percentage")]
    [Range(0f, 100f)] public float phase2HealthPercent = 67f;
    [Tooltip("Phase 3 starts when health drops to or below this percentage (also ends Phase 2). Keep the order: Phase 1.5 > Phase 2 > Phase 3.")]
    [Range(0f, 100f)] public float phase3HealthPercent = 33.33f;

    [Header("Phase 1 - Allowed Attacks")]
    public bool p1_SwordStraight = true;
    public bool p1_SwordRound = true;
    public bool p1_SPro = true;
    public bool p1_Ring = true;
    public bool p1_FarRing = true;
    public bool p1_Teleport = true;
    public bool p1_Bomb1 = true;
    public bool p1_Bomb3 = true;

    [Header("Phase 1.5 - Allowed Attacks & Transition")]
    public bool p1_5_SwordStraight = true;
    public bool p1_5_SwordRound = true;
    public bool p1_5_SPro = true;
    public bool p1_5_Ring = true;
    public bool p1_5_FarRing = true;
    public bool p1_5_Teleport = true;
    public bool p1_5_Bomb1 = true;
    public bool p1_5_Bomb3 = true;
    [Tooltip("Seconds the Operator halts when transitioning into Phase 1.5")]
    public float phase1_5HaltDuration = 2f;

    [Header("Phase 2 Settings")]
    [Tooltip("1st GameObject enabled at the start of Phase 2. Destroyed at the start of Phase 3.")]
    public GameObject phase2Object1;
    [Tooltip("2nd GameObject enabled at the start of Phase 2, together with the 1st (no delay). Destroyed at the start of Phase 3.")]
    public GameObject phase2Object2;
    [Tooltip("Final GameObject of Phase 2: enabled at the end of Phase 2 / the start of Phase 3, at the same moment Object 1 and Object 2 are destroyed")]
    public GameObject phase2FinalObject;

    [Header("Phase 3 - Allowed Attacks & Transition")]
    public bool p3_SwordStraight = true;
    public bool p3_SwordRound = true;
    public bool p3_SPro = true;
    public bool p3_Ring = true;
    public bool p3_FarRing = true;
    public bool p3_Teleport = true;
    public bool p3_Bomb1 = true;
    public bool p3_Bomb3 = true;
    [Tooltip("Seconds the Operator halts when transitioning into Phase 3")]
    public float phase3HaltDuration = 2f;

    // ---------------------------------------------------------------
    //  Decision Requestor
    // ---------------------------------------------------------------
    [Header("Decision Requestor")]
    [Tooltip("Decisions requested per second in Phase 1. 0.33 = once every ~3 seconds. A request is skipped if an attack is ongoing or nothing is off cooldown / in range.")]
    public float decisionFrequencyPhase1 = 0.33f;
    [Tooltip("Decisions requested per second in Phase 1.5")]
    public float decisionFrequencyPhase1_5 = 0.4f;
    [Tooltip("Decisions requested per second in Phase 3")]
    public float decisionFrequencyPhase3 = 0.5f;
    [Tooltip("When nothing can be cast right now (attack running, nothing in range or off cooldown), re-check this often (seconds) instead of waiting a whole decision period")]
    public float decisionRetryInterval = 0.25f;
    [Tooltip("Logs distance, relative speed, the eligible attacks and the pick every time an attack is chosen")]
    public bool logDecisions = false;

    // ---------------------------------------------------------------
    //  Target / Anchor
    // ---------------------------------------------------------------
    [Header("Target")]
    public Transform player;
    [Tooltip("Reference to the LookAt script on the child object. Disabled in Phase 2.")]
    public LookAt childLookAtScript;

    [Header("Mirage Source")]
    public MirageSpell mirageSpell;

    [Header("Anchor")]
    [Tooltip("Center of the arena - Operator won't go beyond anchor range")]
    public Transform anchor;
    public float maxAnchorDistance = 40000f;

    // ---------------------------------------------------------------
    //  Tendrils
    // ---------------------------------------------------------------
    [Header("Tendrils")]
    [Tooltip("OperatorTendrils on this object (auto-found in Start if empty). Blocked for the whole Teleport attack so a caught player doesn't take two attacks at once.")]
    public OperatorTendrils operatorTendrils;
    [Tooltip("Seconds after the player is thrown (or after the Teleport ends / is cancelled) before the tendrils may attack again")]
    public float tendrilReenableDelay = 0.5f;

    // ---------------------------------------------------------------
    //  Chase
    // ---------------------------------------------------------------
    [Header("Chase Settings")]
    public float catchRange = 5f;
    [Tooltip("Acceleration used only while being pulled back to the anchor")]
    public float acceleration = 150f;
    [Tooltip("Hard cap on Operator speed while chasing")]
    public float maxSpeed = 20000f;
    [Tooltip("How fast (units/s^2) the Operator's velocity is steered to the velocity needed for the target relative speed")]
    public float velocityMatchAcceleration = 5000f;
    public float rotationSpeed = 8f;

    [Header("Relative Velocity Curve (x = distance to target)")]
    [Tooltip("Relative speed while 0 < x < swordAttackRange")]
    public float closeRelativeSpeed = 1000f;
    [Tooltip("Beyond swordAttackRange: relative speed = (x - x * base^(offset - x)) / divisor + baseSpeed")]
    public float farSpeedDivisor = 5f;
    public float farSpeedBase = 1000f;
    public float speedCurveBase = 5f;
    public float speedCurveOffset = 5000f;

    [Header("Chase Target (random point inside the sword range around the player)")]
    [Tooltip("The Operator is faster than the player, so every move target is a random point inside a sphere around the player of radius swordAttackRange * this. 1 = the full sword range; below 1 keeps the targets away from the edge so it overshoots out of range less. It only tries to get in range - it is never pinned to the player. If the player is far away the Operator keeps attacking normally and keeps heading for that sphere.")]
    [Range(0.1f, 1f)] public float chaseRangeScale = 0.8f;
    public float wanderUpdateMin = 0.5f;
    public float wanderUpdateMax = 2f;
    public float wanderReachThreshold = 300f;

    [Header("Knockback Settings")]
    public float projectileKnockbackSpeed = 2000f;
    public float knockbackSpeedCap = 2500f;

    [Header("Velocity Lock")]
    public float velocityLockFadeDuration = 0.5f;

    [Header("Mana")]
    public ManaSystem_Operator manaSystem;
    [Tooltip("Below this much mana the Operator can't start any attack and instead moves at the highest possible relative speed to the player. Set it to the cheapest attack cost. 0 = disabled.")]
    public float panicMana = 0f;

    // ---------------------------------------------------------------
    //  Cancel On Hit
    // ---------------------------------------------------------------
    [Header("Cancel Attack On Hit")]
    [Tooltip("Per attack: does a hit on the Operator interrupt it? Only used by CancelAttackOnHit() - call that from your hit/damage code. Phase changes always cancel everything. Far Ring is instant, so it has no toggle.")]
    public bool cancelOnHit_SwordStraight = false;
    public bool cancelOnHit_SwordRound = false;
    public bool cancelOnHit_SPro = false;
    public bool cancelOnHit_Ring = true;
    public bool cancelOnHit_Teleport = true;
    public bool cancelOnHit_Bomb1 = false;
    public bool cancelOnHit_Bomb3 = false;

    // ---------------------------------------------------------------
    //  Attack Ranges
    // ---------------------------------------------------------------
    [Header("Attack Ranges (all masking distances)")]
    [Tooltip("Sword Straight and Sword Round: the player must be within this distance")]
    public float swordAttackRange = 10000f;
    [Tooltip("Sword Straight also needs the relative speed between the player and the operator BELOW this value")]
    public float swordStraightMaxRelativeSpeed = 2000f;
    [Tooltip("S_Pro: swordAttackRange < distance < farRange.  Far Ring: farRange < distance < veryFarRange.  Teleport: distance > farRange")]
    public float farRange = 30000f;
    public float veryFarRange = 60000f;
    [Tooltip("If the player is further than veryFarRange, Teleport overrides every other attack. If Teleport is eligible but the Operator can't afford it yet, it waits for the mana instead of casting something else.")]
    public bool forceTeleportBeyondVeryFar = true;
    [Tooltip("Energy Bombs can only be used while the player is further than this")]
    public float bombMinDistance = 5000f;
    [Tooltip("Ring can only be used while the player is further than this")]
    public float ringAttackMinDistance = 1500f;

    // ---------------------------------------------------------------
    //  S_Pro
    // ---------------------------------------------------------------
    [Header("S_Pro Attack")]
    public List<GameObject> sProObjects = new List<GameObject>();
    public float sProCooldown = 10f;
    public float sProManaCost = 30f;
    public int sProActiveFrames = 360;

    // ---------------------------------------------------------------
    //  Ring Attack
    // ---------------------------------------------------------------
    [Header("Ring Attack")]
    public GameObject ringAttackPrefab;
    public float ringAttackCooldown = 8f;
    public float ringAttackManaCost = 80f;
    public float ringSpawnCollisionRadius = 10f;
    [Tooltip("Seconds the Operator stands still before the ring fires")]
    public float ringAttackDelay = 0f;
    [Tooltip("Active while the Operator is stopped for the ring attack")]
    public GameObject ringAttackTelegraphObject;

    // ---------------------------------------------------------------
    //  Sword
    // ---------------------------------------------------------------
    [Header("Sword Attack Settings")]
    public GameObject swordStraightObject;
    public GameObject swordRoundObject;
    public float swordDelay = 1f;
    public float swordFullDelay = 2.5f;
    public float swordRoundImmobileFrames = 120f;
    public float swordStraightCooldown = 5f;
    public float swordRoundCooldown = 5f;
    public float swordManaCost = 0f;

    // ---------------------------------------------------------------
    //  Far Ring
    // ---------------------------------------------------------------
    [Header("Far Ring Attack")]
    public GameObject farRingObject;
    [Tooltip("Mana spent each time the Far Ring is cast. If the Operator can't afford it, the Far Ring is not an option for that decision.")]
    public float farRingManaCost = 20f;
    [Tooltip("After a Far Ring, the Operator is frozen in place (no movement at all) and makes no attack decisions for this long")]
    public float farRingLockoutDelay = 5f;

    // ---------------------------------------------------------------
    //  Energy Bombs
    // ---------------------------------------------------------------
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

    // ---------------------------------------------------------------
    //  Teleport
    // ---------------------------------------------------------------
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

    // ---------------------------------------------------------------
    //  Private state
    // ---------------------------------------------------------------
    private static readonly AttackType[] AllAttacks = (AttackType[])System.Enum.GetValues(typeof(AttackType));
    private readonly List<AttackType> eligibleBuffer = new List<AttackType>();
    private readonly List<AttackType> affordableBuffer = new List<AttackType>();
    private float decisionTimer;
    private int manaRetryStage = 0;

    private float teleportAttackTimer;
    private bool isPerformingTeleportAttack = false;
    private bool isInTeleportAttackDelay = false;
    private bool teleportPlayerLocked = false;   // true between PrepareForTeleportAttack and the throw / dodge
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

    private bool isPhaseTransitioning = false;
    private Coroutine phaseTransitionCoroutine = null;

    private bool isDead = false;

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
    private AttackType activeSwordType = AttackType.SwordStraight;
    private AttackType activeBombType = AttackType.Bomb1;

    private Vector3 wanderTarget;
    private float wanderTimer = 0f;
    private Rigidbody rb;
    private OperatorHealth operatorHealth;
    private Vector3 currentVelocity;
    private Vector3 lastPlayerPosition;
    private Transform currentTarget;

    // Push spell
    private bool isPushed = false;
    private float pushTimer = 0f;
    private float pushDuration = 1f;
    private float pushMaxSpeed = 4000f;
    private float pushCriticalSpeed = 500f;
    private Vector3 pushDirection = Vector3.zero;

    // Velocity lock (hold-F channel)
    private bool isVelocityLocked = false;
    private float velocityLockFadeTimer = 0f;
    private Vector3 velocityLockStartVelocity;
    private Vector3 velocityLockTargetVelocity;

    public Vector3 PlayerVelocity { get; private set; }
    public bool HasCaughtPlayer { get; private set; }
    public OperatorState CurrentState { get; private set; }
    public bool IsVelocityLocked => isVelocityLocked;
    public float RelativePlayerSpeed => (PlayerVelocity - currentVelocity).magnitude;
    public Vector3 GetCurrentVelocity() => currentVelocity;

    // True from the moment health hits 0. The Operator is frozen and makes no attacks.
    public bool IsDead => isDead;

    // True while mana is below panicMana: no attacks can start, and the Operator chases at max relative speed.
    public bool IsPanicking => manaSystem != null && manaSystem.CurrentMana < panicMana;

    // True for farRingLockoutDelay seconds after a Far Ring: the Operator is frozen in place.
    public bool IsFarRingLocked => farRingLockTimer > 0f;

    private float CurrentDecisionFrequency => phase3 ? decisionFrequencyPhase3 : phase1_5 ? decisionFrequencyPhase1_5 : decisionFrequencyPhase1;

    private float DecisionPeriod => CurrentDecisionFrequency > 0f ? 1f / CurrentDecisionFrequency : float.PositiveInfinity;

    // ---------------------------------------------------------------
    //  Init
    // ---------------------------------------------------------------

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
        decisionTimer = DecisionPeriod;
    }

    // ---------------------------------------------------------------
    //  Update
    // ---------------------------------------------------------------

    void Update()
    {
        if (player == null) return;

        sProTimer -= Time.deltaTime;
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

        UpdateDecisions();
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
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
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

    // ---------------------------------------------------------------
    //  Decision Requestor
    // ---------------------------------------------------------------

    bool CanRequestDecision()
    {
        if (isDead) return false;
        if (!(phase1 || phase1_5 || phase3)) return false;
        if (IsPanicking) return false;
        if (IsAttackBlocked() || farRingLockTimer > 0f) return false;
        if (CurrentState == OperatorState.AnchorPull || isPushed || isVelocityLocked) return false;
        return true;
    }

    void ScheduleNextDecision()
    {
        float period = DecisionPeriod;
        if (manaRetryStage <= 0) decisionTimer = period;
        else if (manaRetryStage == 1) decisionTimer = period * 0.5f;
        else decisionTimer = period * 0.25f;
    }

    void UpdateDecisions()
    {
        if (CurrentDecisionFrequency <= 0f) return;

        decisionTimer -= Time.deltaTime;
        if (decisionTimer > 0f) return;

        // Busy or nothing usable: look again shortly instead of waiting a whole period
        if (!CanRequestDecision()) { decisionTimer = decisionRetryInterval; return; }

        float d = Vector3.Distance(transform.position, player.position);
        eligibleBuffer.Clear();
        foreach (AttackType a in AllAttacks)
            if (IsEligible(a, d)) eligibleBuffer.Add(a);

        if (eligibleBuffer.Count == 0) { decisionTimer = decisionRetryInterval; return; }

        // Only pick from attacks the Operator can afford right now
        affordableBuffer.Clear();
        foreach (AttackType a in eligibleBuffer)
            if (manaSystem == null || manaSystem.CurrentMana >= CostOf(a)) affordableBuffer.Add(a);

        // Beyond veryFarRange, Teleport overrides everything else
        if (forceTeleportBeyondVeryFar && d > veryFarRange && eligibleBuffer.Contains(AttackType.Teleport))
        {
            affordableBuffer.Clear();
            if (manaSystem == null || manaSystem.CurrentMana >= CostOf(AttackType.Teleport))
                affordableBuffer.Add(AttackType.Teleport);
        }

        if (affordableBuffer.Count == 0)
        {
            // Attacks are available but none are affordable: retry in 1/2 gap, then 1/4 gap until something casts
            manaRetryStage++;
            ScheduleNextDecision();
            return;
        }

        AttackType pick = affordableBuffer[Random.Range(0, affordableBuffer.Count)];

        if (logDecisions)
            Debug.Log($"[OperatorAI] d={d:F0} relSpeed={RelativePlayerSpeed:F0} candidates=[{string.Join(", ", affordableBuffer)}] pick={pick}");

        if (CastAttack(pick))
            manaRetryStage = 0;     // back to the normal period
        else
            manaRetryStage++;       // safety net if the mana spend still failed

        ScheduleNextDecision();
    }

    bool AllowedInPhase(AttackType a)
    {
        if (phase1)
            return a switch
            {
                AttackType.SwordStraight => p1_SwordStraight,
                AttackType.SwordRound => p1_SwordRound,
                AttackType.SPro => p1_SPro,
                AttackType.Ring => p1_Ring,
                AttackType.FarRing => p1_FarRing,
                AttackType.Teleport => p1_Teleport,
                AttackType.Bomb1 => p1_Bomb1,
                AttackType.Bomb3 => p1_Bomb3,
                _ => false
            };

        if (phase1_5)
            return a switch
            {
                AttackType.SwordStraight => p1_5_SwordStraight,
                AttackType.SwordRound => p1_5_SwordRound,
                AttackType.SPro => p1_5_SPro,
                AttackType.Ring => p1_5_Ring,
                AttackType.FarRing => p1_5_FarRing,
                AttackType.Teleport => p1_5_Teleport,
                AttackType.Bomb1 => p1_5_Bomb1,
                AttackType.Bomb3 => p1_5_Bomb3,
                _ => false
            };

        if (phase3)
            return a switch
            {
                AttackType.SwordStraight => p3_SwordStraight,
                AttackType.SwordRound => p3_SwordRound,
                AttackType.SPro => p3_SPro,
                AttackType.Ring => p3_Ring,
                AttackType.FarRing => p3_FarRing,
                AttackType.Teleport => p3_Teleport,
                AttackType.Bomb1 => p3_Bomb1,
                AttackType.Bomb3 => p3_Bomb3,
                _ => false
            };

        return false;
    }

    // Eligibility = phase toggle + cooldown + range. Mana is checked separately (it drives the retry timing).
    bool IsEligible(AttackType a, float d)
    {
        if (!AllowedInPhase(a)) return false;

        switch (a)
        {
            case AttackType.SwordStraight:
                return swordStraightTimer <= 0f && d <= swordAttackRange && RelativePlayerSpeed < swordStraightMaxRelativeSpeed;
            case AttackType.SwordRound:
                return swordRoundTimer <= 0f && d <= swordAttackRange;
            case AttackType.SPro:
                return sProTimer <= 0f && d > swordAttackRange && d < farRange;
            case AttackType.Ring:
                return ringAttackPrefab != null && ringAttackTimer <= 0f && d > ringAttackMinDistance;
            case AttackType.FarRing:
                return farRingObject != null && d > farRange && d < veryFarRange;
            case AttackType.Teleport:
                return teleportAttackTimer <= 0f && d > farRange;
            case AttackType.Bomb1:
                return bomb1Object != null && bomb1Timer <= 0f && d > bombMinDistance;
            case AttackType.Bomb3:
                return bomb3Object != null && bomb3Timer <= 0f && d > bombMinDistance;
        }
        return false;
    }

    float CostOf(AttackType a)
    {
        switch (a)
        {
            case AttackType.SwordStraight:
            case AttackType.SwordRound: return swordManaCost;
            case AttackType.SPro: return sProManaCost;
            case AttackType.Ring: return ringAttackManaCost;
            case AttackType.FarRing: return farRingManaCost;
            case AttackType.Teleport: return teleportAttackCost;
            case AttackType.Bomb1: return bomb1ManaCost;
            case AttackType.Bomb3: return bomb3ManaCost;
            default: return 0f;
        }
    }

    // Returns false only when the Operator lacks the mana.
    bool CastAttack(AttackType a)
    {
        float cost = CostOf(a);
        if (manaSystem != null && manaSystem.CurrentMana < cost) return false;

        switch (a)
        {
            case AttackType.SwordStraight:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                swordStraightTimer = swordStraightCooldown;
                activeSwordType = AttackType.SwordStraight;
                swordCoroutine = StartCoroutine(SwordStraightRoutine());
                break;

            case AttackType.SwordRound:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                swordRoundTimer = swordRoundCooldown;
                activeSwordType = AttackType.SwordRound;
                swordCoroutine = StartCoroutine(SwordRoundFrameRoutine());
                break;

            case AttackType.SPro:
                // Mana is spent per shot inside the routine
                sProTimer = sProCooldown;
                sProCoroutine = StartCoroutine(SProAttackRoutine());
                break;

            case AttackType.Ring:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                ringAttackTimer = ringAttackCooldown;
                ringAttackCoroutine = StartCoroutine(RingAttackRoutine());
                break;

            case AttackType.FarRing:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;

                farRingObject.SetActive(false);
                farRingObject.SetActive(true);
                farRingLockTimer = farRingLockoutDelay;

                // Freeze immediately; IsMovementBlocked() keeps it frozen for the whole lockout
                currentVelocity = Vector3.zero;
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                break;

            case AttackType.Teleport:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                teleportAttackTimer = teleportAttackCooldown;
                teleportAttackCoroutine = StartCoroutine(TeleportAttackRoutine());
                break;

            case AttackType.Bomb1:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                bomb1Timer = bomb1Cooldown;
                activeBombType = AttackType.Bomb1;
                bombCoroutine = StartCoroutine(BombRoutine(bomb1Object, bomb1Wait));
                break;

            case AttackType.Bomb3:
                if (manaSystem != null && !manaSystem.UseMana(cost)) return false;
                bomb3Timer = bomb3Cooldown;
                activeBombType = AttackType.Bomb3;
                bombCoroutine = StartCoroutine(BombRoutine(bomb3Object, bomb3Wait));
                break;
        }
        return true;
    }

    // ---------------------------------------------------------------
    //  Blocking
    // ---------------------------------------------------------------

    private bool IsMovementBlocked()
    {
        return isDead || isPerformingSPro || isPerformingRingAttack || isPerformingTeleportAttack || isPerformingBomb ||
               isPhaseTransitioning || phase2 || isSwordImmobile || farRingLockTimer > 0f;
    }

    public bool IsAttackBlocked()
    {
        return isDead || isPerformingSPro || isPerformingRingAttack || isPerformingTeleportAttack || isPerformingBomb ||
               isPhaseTransitioning || phase2 || isPerformingSwordAttack;
    }

    // ---------------------------------------------------------------
    //  S_Pro
    // ---------------------------------------------------------------

    IEnumerator SProAttackRoutine()
    {
        isPerformingSPro = true;
        activeSProObjects.Clear();
        sProSubCoroutines.Clear();
        activeSProRoutines = 0;

        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        int fireCount = Random.Range(1, 5);
        for (int i = 0; i < fireCount; i++)
        {
            if (manaSystem != null && !manaSystem.UseMana(sProManaCost)) break;
            if (sProObjects != null && sProObjects.Count > 0)
            {
                GameObject chosenObj = sProObjects[Random.Range(0, sProObjects.Count)];
                if (chosenObj != null)
                {
                    chosenObj.SetActive(true);
                    if (!activeSProObjects.Contains(chosenObj)) activeSProObjects.Add(chosenObj);
                    sProSubCoroutines.Add(StartCoroutine(DisableObjectAfterFrames(chosenObj, sProActiveFrames)));
                }
            }
            yield return new WaitForSeconds(Random.Range(0.2f, 0.5f));
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

    // ---------------------------------------------------------------
    //  Ring Attack
    // ---------------------------------------------------------------

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
            Vector3 displacement = player.position - transform.position;
            Vector3 spawnPosition = transform.position + displacement * 0.98f;
            GameObject spawnedRing = Instantiate(ringAttackPrefab, spawnPosition, transform.rotation);

            if (Physics.CheckSphere(spawnPosition, ringSpawnCollisionRadius, LayerMask.GetMask("SceneObject")))
                Destroy(spawnedRing);
        }

        if (ringAttackTelegraphObject != null) ringAttackTelegraphObject.SetActive(false);

        isPerformingRingAttack = false;
        ringAttackCoroutine = null;
    }

    // ---------------------------------------------------------------
    //  Sword Attacks
    // ---------------------------------------------------------------

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
        for (int i = 0; i < totalFrames; i++) yield return new WaitForFixedUpdate();

        if (activeSwordObject != null) activeSwordObject.SetActive(false);
        activeSwordObject = null;
        isSwordImmobile = false;
        isPerformingSwordAttack = false;
        swordCoroutine = null;
    }

    // ---------------------------------------------------------------
    //  Energy Bombs
    // ---------------------------------------------------------------

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

    // ---------------------------------------------------------------
    //  Wander target (spheres) & target selection
    // ---------------------------------------------------------------

    void RefreshWanderTarget()
    {
        if (player == null) return;

        // Next move target = a random point inside the sword-attack range around the player.
        // The Operator is faster than the player, so this keeps it in sword range without pinning it
        // to the player. If the player is far away the point is still near the player, so it keeps
        // pushing in (attacks keep firing normally from the decision requestor meanwhile).
        float radius = swordAttackRange * chaseRangeScale;
        wanderTarget = player.position + Random.insideUnitSphere * radius;

        wanderTimer = Random.Range(wanderUpdateMin, wanderUpdateMax);
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

    // ---------------------------------------------------------------
    //  State & Movement
    // ---------------------------------------------------------------

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

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, transform.up),
                                              rotationSpeed * Time.fixedDeltaTime);
    }

    // x = distance to target.
    //   0 < x < swordAttackRange : closeRelativeSpeed (1000)
    //   x > swordAttackRange     : (x - x / 5^(x - 5000)) / 5 + 1000   [x * 5^(5000 - x) form]
    float GetTargetRelativeSpeed(float x)
    {
        if (x < swordAttackRange) return closeRelativeSpeed;

        float falloff = x * Mathf.Pow(speedCurveBase, speedCurveOffset - x);
        falloff = Mathf.Min(falloff, x); // guards against overflow if swordAttackRange is set below the offset
        return (x - falloff) / farSpeedDivisor + farSpeedBase;
    }

    void HandleMovement(Vector3 chasePos, float distToChase, float distanceToAnchor)
    {
        if (isVelocityLocked)
        {
            velocityLockFadeTimer += Time.fixedDeltaTime;
            float lt = velocityLockFadeDuration > 0f ? Mathf.Clamp01(velocityLockFadeTimer / velocityLockFadeDuration) : 1f;
            currentVelocity = Vector3.Lerp(velocityLockStartVelocity, velocityLockTargetVelocity, lt);
            ApplyVelocity();
            return;
        }

        if (isPushed)
        {
            pushTimer -= Time.fixedDeltaTime;
            if (pushTimer <= 0f) { isPushed = false; currentVelocity = pushDirection * pushCriticalSpeed; }
            else currentVelocity = pushDirection * Mathf.Lerp(pushMaxSpeed, pushCriticalSpeed, 1f - (pushTimer / pushDuration));
            ApplyVelocity();
            return;
        }

        switch (CurrentState)
        {
            case OperatorState.AnchorPull:
                currentVelocity = Vector3.ClampMagnitude(
                    currentVelocity + (anchor.position - transform.position).normalized * (acceleration * 4f) * Time.fixedDeltaTime,
                    maxSpeed * 4f);
                break;

            case OperatorState.Chasing:
                {
                    Vector3 toChase = chasePos - transform.position;
                    Vector3 dir = toChase.sqrMagnitude > 0.0001f ? toChase.normalized : Vector3.zero;

                    float x = Vector3.Distance(transform.position, currentTarget.position);

                    float relSpeed;
                    if (IsPanicking)
                        relSpeed = maxSpeed;    // highest possible, and no slowing down inside catchRange
                    else
                        relSpeed = toChase.magnitude <= catchRange ? 0f : GetTargetRelativeSpeed(x);

                    // Keep the operator's velocity relative to the target at relSpeed, aimed at the chase point
                    Vector3 targetVel = currentTarget == player ? PlayerVelocity : Vector3.zero;
                    Vector3 desired = Vector3.ClampMagnitude(targetVel + dir * relSpeed, maxSpeed);

                    currentVelocity = Vector3.MoveTowards(currentVelocity, desired, velocityMatchAcceleration * Time.fixedDeltaTime);
                    break;
                }
        }

        ApplyVelocity();
    }

    void ApplyVelocity()
    {
        if (rb != null) rb.linearVelocity = currentVelocity;
        else transform.position += currentVelocity * Time.fixedDeltaTime;
    }

    // ---------------------------------------------------------------
    //  Phases
    // ---------------------------------------------------------------

    void CheckHealthPhases()
    {
        if (operatorHealth == null) return;
        if (isDead) return;

        // Health hit 0: stop everything and stay frozen
        if (operatorHealth.CurrentHealth <= 0f)
        {
            Die();
            return;
        }

        float healthPct = operatorHealth.CurrentHealth / operatorHealth.MaxHealth * 100f;

        if (healthPct <= phase3HealthPercent && !phase3)
        {
            bool wasInPhase2 = phase2;

            phase1 = phase1_5 = phase2 = false;
            phase3 = true;

            // End of Phase 2: its two objects are destroyed and the final object takes over
            if (phase2Object1 != null) { Destroy(phase2Object1); phase2Object1 = null; }
            if (phase2Object2 != null) { Destroy(phase2Object2); phase2Object2 = null; }
            if (wasInPhase2 && phase2FinalObject != null) phase2FinalObject.SetActive(true);

            if (childLookAtScript != null) childLookAtScript.enabled = true;
            CancelAttack();
            if (phaseTransitionCoroutine != null) StopCoroutine(phaseTransitionCoroutine);
            phaseTransitionCoroutine = StartCoroutine(PhaseTransitionRoutine(phase3HaltDuration));
        }
        else if (healthPct <= phase2HealthPercent && !phase2 && !phase3)
        {
            phase1 = phase1_5 = false;
            phase2 = true;
            if (childLookAtScript != null) childLookAtScript.enabled = false;
            CancelAttack();

            // The Operator stops dead (IsMovementBlocked() keeps it still for the whole of Phase 2)
            currentVelocity = Vector3.zero;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

            // Phase 2 has no timer: the particle and BOTH objects start together, right as it stops moving.
            // Phase 2 simply lasts until Phase 3 begins.
            if (phase2Particle != null) phase2Particle.SetActive(true);
            if (phase2Object1 != null) phase2Object1.SetActive(true);
            if (phase2Object2 != null) phase2Object2.SetActive(true);
        }
        else if (healthPct <= phase1_5HealthPercent && !phase1_5 && !phase2 && !phase3)
        {
            phase1 = false;
            phase1_5 = true;
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

    // ---------------------------------------------------------------
    //  Death
    // ---------------------------------------------------------------

    void Die()
    {
        // Set the flag FIRST so ReleaseTendrilsAfterTeleport (called from the teleport cancel) stays quiet
        isDead = true;

        // Same full stop the Compress spell uses: all attacks (incl. Teleport + time freeze), far ring, tendrils, push, velocity lock
        CancelEverything();

        // Keep the tendrils off for good
        if (operatorTendrils != null) operatorTendrils.BlockAttacks();

        // The child LookAt would keep rotating it otherwise
        if (childLookAtScript != null) childLookAtScript.enabled = false;
    }

    // ---------------------------------------------------------------
    //  Teleport Attack
    // ---------------------------------------------------------------

    // Lets the tendrils attack again tendrilReenableDelay seconds from now.
    void ReleaseTendrilsAfterTeleport()
    {
        if (isDead) return;     // tendrils stay off once the Operator is dead
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
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        BasicMovement_2 playerMovement = player.GetComponent<BasicMovement_2>();

        if (teleportObject1 != null) teleportObject1.SetActive(true);
        isInTeleportAttackDelay = true;

        if (teleportDelay1 > 0f) yield return new WaitForSeconds(teleportDelay1);

        isInTeleportAttackDelay = false;
        if (teleportObject1 != null) teleportObject1.SetActive(false);

        if (playerMovement != null)
        {
            playerMovement.PrepareForTeleportAttack();
            teleportPlayerLocked = true;
        }

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

        while (currentDistance < totalDistance)
        {
            elapsedTime += Time.unscaledDeltaTime;

            if (!playerDodged && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                playerDodged = true;

                if (playerMovement != null)
                {
                    player.position -= playerMovement.transform.forward * playerMovement.teleportDistance;
                    playerMovement.CancelTeleportLock();
                    teleportPlayerLocked = false;
                }

                foreach (MonoBehaviour mb in _disabledScripts)
                    if (mb != null) mb.enabled = true;
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
            if (mb != null) mb.enabled = true;
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
                transform.rotation = Quaternion.LookRotation(directionToPlayer, transform.up);

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
            teleportPlayerLocked = false;   // the throw takes over from here
        }

        isPerformingTeleportAttack = false;
        teleportAttackCoroutine = null;

        // Tendrils come back tendrilReenableDelay (0.5s) after the player is thrown
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

    // ---------------------------------------------------------------
    //  Public API (spells / projectiles)
    // ---------------------------------------------------------------

    public void ApplyMomentumHit(float reductionPercent)
    {
        if (isDead) return;

        // Frozen during the Far Ring lockout - nothing may move the Operator
        if (farRingLockTimer > 0f) return;

        float retainedSpeed = Mathf.Max(0f, currentVelocity.magnitude * (1f - reductionPercent));
        currentVelocity = currentVelocity.normalized * retainedSpeed;
        if (rb != null) rb.linearVelocity = currentVelocity;
    }

    public void ApplyKnockback(Vector3 hitNormal, float speed)
    {
        if (isDead) return;

        // Frozen during the Far Ring lockout - nothing may move the Operator
        if (farRingLockTimer > 0f) return;

        Vector3 newVelocity = currentVelocity + hitNormal.normalized * speed;

        if (Vector3.Dot(newVelocity.normalized, currentVelocity.normalized) > 0 && newVelocity.magnitude > knockbackSpeedCap)
            newVelocity = newVelocity.normalized * knockbackSpeedCap;

        currentVelocity = newVelocity;
        if (rb != null) rb.linearVelocity = currentVelocity;
    }

    public void StartPush(Vector3 direction, float maxSpd, float criticalSpeed, float duration)
    {
        if (isDead) return;

        isPushed = true;
        pushDirection = direction.normalized;
        pushMaxSpeed = maxSpd;
        pushCriticalSpeed = criticalSpeed;
        pushDuration = duration;
        pushTimer = duration;
    }

    public void StartVelocityLock(Vector3 targetVelocity)
    {
        if (isDead) return;

        isVelocityLocked = true;
        velocityLockFadeTimer = 0f;
        velocityLockStartVelocity = currentVelocity;
        velocityLockTargetVelocity = targetVelocity;
    }

    public void ReleaseVelocityLock()
    {
        isVelocityLocked = false;
    }

    // Full cancel (phase changes etc.): interrupts everything.
    public void CancelAttack() => CancelAttackInternal(false);

    // Call this from hit/damage code instead of CancelAttack().
    // Only interrupts attacks whose cancelOnHit_ toggle is on; phase transitions are left alone.
    public void CancelAttackOnHit() => CancelAttackInternal(true);

    // Called by CompressSpell the moment the orb connects (and by Die() when health hits 0). Cancels every
    // ongoing process on the Operator - all attacks (INCLUDING Teleport and its time-freeze / player lock),
    // the far ring, the tendrils, push and velocity lock - and stops the Operator dead. The Phase 1.5 / Phase 3
    // transition halt is left running so the encounter's phase flow isn't broken.
    // Call this BEFORE disabling this component: disabling a MonoBehaviour does not stop its coroutines.
    public void CancelEverything()
    {
        CancelAttackInternal(false, true);

        if (farRingObject != null) farRingObject.SetActive(false);
        farRingLockTimer = 0f;

        isPushed = false;
        isVelocityLocked = false;

        currentVelocity = Vector3.zero;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        if (operatorTendrils != null) operatorTendrils.CancelAttack();
    }

    void CancelAttackInternal(bool fromHit, bool keepPhaseRoutines = false)
    {
        if (!fromHit)
        {
            // A phase change resets the decision rhythm
            manaRetryStage = 0;
            decisionTimer = DecisionPeriod;
        }

        if (!fromHit && !keepPhaseRoutines && isPhaseTransitioning)
        {
            if (phaseTransitionCoroutine != null) StopCoroutine(phaseTransitionCoroutine);
            phaseTransitionCoroutine = null;
            isPhaseTransitioning = false;
        }

        if (isPerformingSwordAttack &&
            (!fromHit || (activeSwordType == AttackType.SwordStraight ? cancelOnHit_SwordStraight : cancelOnHit_SwordRound)))
        {
            if (swordCoroutine != null) StopCoroutine(swordCoroutine);
            swordCoroutine = null;
            if (activeSwordObject != null) activeSwordObject.SetActive(false);
            if (swordStraightObject != null) swordStraightObject.SetActive(false);
            if (swordRoundObject != null) swordRoundObject.SetActive(false);
            activeSwordObject = null;
            isPerformingSwordAttack = isSwordImmobile = false;
        }

        if (isPerformingSPro && (!fromHit || cancelOnHit_SPro))
        {
            if (sProCoroutine != null) StopCoroutine(sProCoroutine);
            sProCoroutine = null;
            foreach (Coroutine c in sProSubCoroutines) if (c != null) StopCoroutine(c);
            sProSubCoroutines.Clear();
            activeSProRoutines = 0;
            foreach (GameObject obj in activeSProObjects) if (obj != null) obj.SetActive(false);
            activeSProObjects.Clear();
            isPerformingSPro = false;
        }

        if (isPerformingRingAttack && (!fromHit || cancelOnHit_Ring))
        {
            if (ringAttackCoroutine != null) StopCoroutine(ringAttackCoroutine);
            ringAttackCoroutine = null;
            if (ringAttackTelegraphObject != null) ringAttackTelegraphObject.SetActive(false);
            isInRingAttackDelay = isPerformingRingAttack = false;
        }

        if (isPerformingBomb &&
            (!fromHit || (activeBombType == AttackType.Bomb1 ? cancelOnHit_Bomb1 : cancelOnHit_Bomb3)))
        {
            if (bombCoroutine != null) StopCoroutine(bombCoroutine);
            bombCoroutine = null;
            if (bomb1Object != null) bomb1Object.SetActive(false);
            if (bomb3Object != null) bomb3Object.SetActive(false);
            isPerformingBomb = false;
        }

        if ((isInTeleportAttackDelay || isPerformingTeleportAttack) && (!fromHit || cancelOnHit_Teleport))
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

            // If the player was locked by the teleport (PrepareForTeleportAttack / lockInput),
            // give control back - otherwise a cancelled teleport leaves them frozen.
            if (teleportPlayerLocked && player != null)
            {
                BasicMovement_2 pm = player.GetComponent<BasicMovement_2>();
                if (pm != null)
                {
                    pm.CancelTeleportLock();
                    pm.lockInput = false;
                }
            }
            teleportPlayerLocked = false;

            isInTeleportAttackDelay = isPerformingTeleportAttack = false;

            // Teleport is over, so the tendrils may come back (after the usual delay)
            ReleaseTendrilsAfterTeleport();
        }
    }
}