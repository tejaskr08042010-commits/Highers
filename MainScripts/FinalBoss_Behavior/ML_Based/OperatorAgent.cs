using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class OperatorAgent : Agent
{
    const int ObsSize = 16;

    [Header("References")]
    public OperatorAI_ML operatorAI;
    public Transform player;
    public OperatorHealth operatorHealth;
    public PlayerHealth playerHealth;
    public Transform anchor;

    [Header("Observation Scales")]
    public float distanceScale = 10000f;
    public float playerMaxSpeed = 4000f;
    public float relativeMaxSpeed = 8000f;

    [Header("Rewards")]
    public float castCostWeight = 0.1f;
    public float farRingCastPenalty = 0.02f;
    public float damageNormalizer = 100f;
    public float damageWeight = 0.5f;
    public float killReward = 1f;
    public float operatorDeathPenalty = 1f;

    [Header("Episode")]
    public float maxDistance = 40000f;

    Vector3 initialOperatorPos, lastPlayerPos;
    float decisionInterval = 0.2f;
    bool killed;
    float damageDealt;
    readonly int[] casts = new int[6];
    static readonly string[] castNames = { "", "SwordStraight", "SwordRound", "SPro", "FarRing", "Teleport" };

    public override void Initialize()
    {
        var requester = GetComponent<DecisionRequester>();
        decisionInterval = Time.fixedDeltaTime * (requester != null ? requester.DecisionPeriod : 1);

        initialOperatorPos = operatorAI.transform.position;
        operatorAI.mlControlled = true;
        if (playerHealth != null)
        {
            playerHealth.OnDamaged += HandlePlayerDamaged;
            playerHealth.OnPlayerDeath += OnPlayerKilled;
        }
    }

    public override void OnEpisodeBegin()
    {
        Academy.Instance.StatsRecorder.Add("Operator/KillRate", killed ? 1f : 0f);
        Academy.Instance.StatsRecorder.Add("Operator/DamagePerEpisode", damageDealt);
        killed = false;
        damageDealt = 0f;
        for (int i = 1; i <= 5; i++)
        {
            Academy.Instance.StatsRecorder.Add("Operator/Casts_" + castNames[i], casts[i]);
            casts[i] = 0;
        }

        operatorAI.ResetForEpisode(initialOperatorPos);
        if (playerHealth != null) playerHealth.RestoreToMax();

        if (player != null)
        {
            player.position = initialOperatorPos + new Vector3(Random.Range(-2000f, 2000f), 0f, Random.Range(1000f, 3000f));
            if (player.TryGetComponent(out Rigidbody prb))
            {
                prb.linearVelocity = Vector3.zero;
                prb.angularVelocity = Vector3.zero;
            }
            lastPlayerPos = player.position;
            operatorAI.SyncPlayer();
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (player == null || operatorAI == null)
        {
            for (int i = 0; i < ObsSize; i++) sensor.AddObservation(0f);
            return;
        }

        sensor.AddObservation(Mathf.Clamp01(Vector3.Distance(operatorAI.transform.position, player.position) / distanceScale));

        Vector3 playerVel = (player.position - lastPlayerPos) / decisionInterval;
        lastPlayerPos = player.position;
        sensor.AddObservation(Mathf.Clamp(playerVel.x / playerMaxSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(playerVel.y / playerMaxSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(playerVel.z / playerMaxSpeed, -1f, 1f));

        sensor.AddObservation(Mathf.Clamp01((operatorAI.GetCurrentVelocity() - playerVel).magnitude / relativeMaxSpeed));
        sensor.AddObservation(playerHealth != null ? playerHealth.HealthPercentage : 1f);
        sensor.AddObservation(operatorAI.GetManaFraction());

        Vector3 toPlayer = (player.position - operatorAI.transform.position).normalized;
        sensor.AddObservation(operatorAI.transform.InverseTransformDirection(toPlayer));

        for (int i = 1; i <= 5; i++) sensor.AddObservation(operatorAI.CooldownFraction(i));
        sensor.AddObservation(operatorAI.IsAttackBlocked() ? 1f : 0f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        for (int i = 1; i <= 5; i++)
            if (!operatorAI.CanUse(i)) actionMask.SetActionEnabled(0, i, false);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        if (action > 0 && operatorAI.CanUse(action))
        {
            operatorAI.TryUse(action);
            casts[action]++;
            AddReward(action == 4 ? -farRingCastPenalty : -castCostWeight * operatorAI.CostOf(action) / operatorAI.MaxMana);
        }

        if (operatorHealth != null && operatorHealth.CurrentHealth <= 0f)
        {
            AddReward(-operatorDeathPenalty);
            EndEpisode();
            return;
        }

        if (anchor != null && Vector3.Distance(operatorAI.transform.position, anchor.position) > maxDistance)
            EpisodeInterrupted();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var k = UnityEngine.InputSystem.Keyboard.current;
        int a = 0;
        if (k != null) a = k.digit1Key.isPressed ? 1 : k.digit2Key.isPressed ? 2 : k.digit3Key.isPressed ? 3 : k.digit4Key.isPressed ? 4 : k.digit5Key.isPressed ? 5 : 0;
        var discreteOut = actionsOut.DiscreteActions;
        discreteOut[0] = a;
    }

    void HandlePlayerDamaged(float amount)
    {
        damageDealt += amount;
        AddReward(amount / damageNormalizer * damageWeight);
    }

    void OnPlayerKilled()
    {
        killed = true;
        AddReward(killReward);
        EndEpisode();
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandlePlayerDamaged;
            playerHealth.OnPlayerDeath -= OnPlayerKilled;
        }
    }
}
