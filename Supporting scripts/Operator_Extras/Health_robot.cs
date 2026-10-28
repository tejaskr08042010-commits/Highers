using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class RobotHealthSystem : MonoBehaviour
{
    [Header("Robot Body Parts Configuration")]
    [Tooltip("List of all robot body parts - should match PunchFracture's robotBodyParts list")]
    public List<GameObject> robotBodyParts = new List<GameObject>();

    [Header("Animator Configuration")]
    [Tooltip("Reference to the robot's Animator component")]
    public Animator robotAnimator;

    [Header("Health Configuration")]
    [Tooltip("Health values for each body part (matches robotBodyParts list by index)")]
    public List<float> bodyPartMaxHealth = new List<float>();

    [Tooltip("Current health for each body part (runtime values)")]
    [SerializeField] private List<float> bodyPartCurrentHealth = new List<float>();

    [Header("Parry System")]
    [Tooltip("Is the robot currently in parry state?")]
    public bool isParrying = false;

    [Tooltip("How long parry state lasts")]
    public float parryDuration = 0.6f;

    [Tooltip("Parry window - time when parry is active")]
    public float parryWindowStart = 0.1f;
    public float parryWindowEnd = 0.4f;

    [Tooltip("Damage reduction when parrying (0-1, where 0 = no damage, 1 = full damage)")]
    [Range(0f, 1f)]
    public float parryDamageReduction = 0.1f;

    [Header("Deflect Animation")]
    [Tooltip("Reference to the DeflectLayerController")]
    private DeflectLayerController deflectController;

    [Header("Detachment Settings")]
    [Tooltip("Mass to assign to detached parts")]
    public float detachedPartMass = 2f;

    [Tooltip("Force multiplier applied when part detaches")]
    public float detachmentForceMultiplier = 1.5f;

    [Tooltip("Should detached parts use gravity?")]
    public bool detachedPartsUseGravity = true;

    [Header("Head Rotation Settings")]
    [Tooltip("Maximum damage value for full head rotation (50000 = max rotation)")]
    public float maxDamageForFullRotation = 50000f;

    [Tooltip("Minimum Y rotation angle when head is hit")]
    public float minHeadRotationY = 50f;

    [Tooltip("Maximum Y rotation angle when head is hit")]
    public float maxHeadRotationY = 135f;

    [Header("Health Regeneration (Optional)")]
    [Tooltip("Should parts regenerate health over time?")]
    public bool enableHealthRegeneration = false;

    [Tooltip("Health regeneration rate per second")]
    public float healthRegenRate = 1f;

    [Tooltip("Delay before regeneration starts after taking damage")]
    public float regenDelay = 3f;

    [Header("Visual/Audio Feedback")]
    [Tooltip("Particle effect to spawn when part is damaged")]
    public GameObject damageParticleEffect;

    [Tooltip("Particle effect to spawn when part is destroyed")]
    public GameObject destructionParticleEffect;

    [Tooltip("Particle effect to spawn on successful parry")]
    public GameObject parryParticleEffect;

    [Tooltip("Audio source for damage sounds")]
    public AudioSource audioSource;

    [Tooltip("Sound to play when part takes damage")]
    public AudioClip damageSound;

    [Tooltip("Sound to play when part is destroyed")]
    public AudioClip destructionSound;

    [Tooltip("Sound to play on successful parry")]
    public AudioClip parrySound;

    [Header("UI Health Display")]
    [Tooltip("Text to display average robot health")]
    public TextMeshProUGUI averageHealthText;

    [Tooltip("Text to display Mesh_0.001 health")]
    public TextMeshProUGUI mesh0001HealthText;

    [Header("Debug Information")]
    [SerializeField] private List<bool> partIsDetached = new List<bool>();
    [SerializeField] public List<float> lastDamageTime = new List<float>();
    [SerializeField] private float parryStartTime = 0f;

    public System.Action<GameObject, float> OnPartDamaged;
    public System.Action<GameObject> OnPartDestroyed;
    public System.Action<GameObject, float> OnSuccessfulParry;

    private GameObject mesh0001Part;

    private void Start()
    {
        InitializeHealthSystem();
        FindMesh0001Part();
    }

    private void Update()
    {
        if (enableHealthRegeneration)
        {
            HandleHealthRegeneration();
        }

        UpdateHealthUI();
    }

    private void FindMesh0001Part()
    {
        foreach (GameObject part in robotBodyParts)
        {
            if (part != null && part.name == "Mesh_0.001")
            {
                mesh0001Part = part;
                Debug.Log("Found Mesh_0.001 for health display");
                break;
            }
        }
    }

    private void UpdateHealthUI()
    {
        // Calculate average health
        float totalHealth = 0f;
        float totalMaxHealth = 0f;

        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (robotBodyParts[i] != null && !partIsDetached[i])
            {
                totalHealth += bodyPartCurrentHealth[i];
                totalMaxHealth += bodyPartMaxHealth[i];
            }
        }

        float averageHealthPercent = totalMaxHealth > 0 ? (totalHealth / totalMaxHealth) * 100f : 0f;

        // Update average health text
        if (averageHealthText != null)
        {
            averageHealthText.text = $"Avg Health: {averageHealthPercent:F1}%";
        }

        // Update Mesh_0.001 health text
        if (mesh0001HealthText != null)
        {
            if (mesh0001Part != null)
            {
                if (IsPartDetached(mesh0001Part))
                {
                    mesh0001HealthText.text = "Core: DESTROYED";
                }
                else
                {
                    float currentHealth = GetPartCurrentHealth(mesh0001Part);
                    int index = robotBodyParts.IndexOf(mesh0001Part);
                    float maxHealth = bodyPartMaxHealth[index];
                    float healthPercent = (currentHealth / maxHealth) * 100f;

                    mesh0001HealthText.text = $"Core: {healthPercent:F1}% ({currentHealth:F0}/{maxHealth:F0})";
                }
            }
            else
            {
                mesh0001HealthText.text = "Core: N/A";
            }
        }
    }

    private void InitializeHealthSystem()
    {
        deflectController = GetComponent<DeflectLayerController>();
        if (deflectController == null)
        {
            Debug.LogWarning("DeflectLayerController not found on this GameObject! Parry animations won't work.");
        }

        int partCount = robotBodyParts.Count;

        while (bodyPartMaxHealth.Count < partCount)
        {
            bodyPartMaxHealth.Add(100f);
        }

        while (bodyPartCurrentHealth.Count < partCount)
        {
            bodyPartCurrentHealth.Add(0f);
        }

        while (partIsDetached.Count < partCount)
        {
            partIsDetached.Add(false);
        }

        while (lastDamageTime.Count < partCount)
        {
            lastDamageTime.Add(0f);
        }

        for (int i = 0; i < partCount; i++)
        {
            if (i < bodyPartMaxHealth.Count)
            {
                bodyPartCurrentHealth[i] = bodyPartMaxHealth[i];
            }
        }

        Debug.Log($"Robot Health System initialized with {partCount} body parts");
        LogHealthStatus();
    }

    public void StartParry()
    {
        isParrying = true;
        parryStartTime = Time.time;
        Debug.Log("=== PARRY STARTED ===");
    }

    public void StopParry()
    {
        isParrying = false;
        Debug.Log("=== PARRY ENDED ===");
    }

    private bool IsInParryWindow()
    {
        if (!isParrying) return false;

        float parryTime = Time.time - parryStartTime;
        return parryTime >= parryWindowStart && parryTime <= parryWindowEnd;
    }

    public void ApplyDamageToBodyPart(GameObject damagedPart, float damageAmount, Vector3 damageDirection)
    {
        if (damagedPart == null)
        {
            Debug.LogWarning("ApplyDamageToBodyPart called with null part!");
            return;
        }

        int partIndex = robotBodyParts.IndexOf(damagedPart);

        if (partIndex == -1)
        {
            Debug.LogWarning($"Body part '{damagedPart.name}' not found in robotBodyParts list!");
            return;
        }

        if (partIsDetached[partIndex])
        {
            Debug.Log($"Part '{damagedPart.name}' is already detached - no damage applied");
            return;
        }

        bool successfulParry = IsInParryWindow();
        float finalDamage = damageAmount;

        if (successfulParry)
        {
            finalDamage = damageAmount * parryDamageReduction;

            if (deflectController != null)
            {
                deflectController.TriggerDeflect();
            }

            SpawnParryEffects(damagedPart);
            OnSuccessfulParry?.Invoke(damagedPart, damageAmount);

            Debug.Log($"=== SUCCESSFUL PARRY ===");
            Debug.Log($"Original damage: {damageAmount:F1} → Reduced to: {finalDamage:F1}");
            Debug.Log($"Damage reduction: {(1f - parryDamageReduction) * 100f:F0}%");
        }

        float previousHealth = bodyPartCurrentHealth[partIndex];
        bodyPartCurrentHealth[partIndex] = Mathf.Max(0, bodyPartCurrentHealth[partIndex] - finalDamage);
        float actualDamage = previousHealth - bodyPartCurrentHealth[partIndex];

        lastDamageTime[partIndex] = Time.time;

        Debug.Log($"=== DAMAGE APPLIED ===");
        Debug.Log($"Part: {damagedPart.name}");
        Debug.Log($"Damage: {actualDamage:F1} (requested: {damageAmount:F1})");
        Debug.Log($"Health: {bodyPartCurrentHealth[partIndex]:F1}/{bodyPartMaxHealth[partIndex]:F1}");
        Debug.Log($"Parry Status: {(successfulParry ? "SUCCESS" : "NONE")}");

        if (damagedPart.name == "Head")
        {
            float headDamage = successfulParry ? finalDamage : damageAmount;
            RotateHeadOnDamage(damagedPart, headDamage);
        }

        if (!successfulParry)
        {
            SpawnDamageEffects(damagedPart, actualDamage);
        }

        OnPartDamaged?.Invoke(damagedPart, actualDamage);

        if (bodyPartCurrentHealth[partIndex] <= 0)
        {
            DetachBodyPart(partIndex, damageDirection);
        }
    }

    private void SpawnParryEffects(GameObject parryPart)
    {
        if (parryParticleEffect != null)
        {
            GameObject effect = Instantiate(parryParticleEffect, parryPart.transform.position, Quaternion.identity);
            Destroy(effect, 2f);
        }

        if (audioSource != null && parrySound != null)
        {
            audioSource.PlayOneShot(parrySound, 0.7f);
        }
    }

    private void RotateHeadOnDamage(GameObject headPart, float damageAmount)
    {
        float rotationIntensity = Mathf.Clamp01(damageAmount / maxDamageForFullRotation);
        bool rotateLeft = Random.Range(0f, 1f) > 0.5f;

        float targetYRotation;
        if (rotateLeft)
        {
            targetYRotation = Mathf.Lerp(90f, minHeadRotationY, rotationIntensity);
        }
        else
        {
            targetYRotation = Mathf.Lerp(90f, maxHeadRotationY, rotationIntensity);
        }

        Vector3 currentRotation = headPart.transform.localEulerAngles;
        headPart.transform.localEulerAngles = new Vector3(currentRotation.x, targetYRotation, currentRotation.z);

        Debug.Log($"=== HEAD ROTATION ===");
        Debug.Log($"Damage: {damageAmount:F1}");
        Debug.Log($"Intensity: {rotationIntensity:F2}");
        Debug.Log($"Direction: {(rotateLeft ? "Left" : "Right")}");
        Debug.Log($"Target Y Rotation: {targetYRotation:F1}°");
    }

    [ContextMenu("Apply Raw Damage to All Parts")]
    public void ApplyRawDamageToAllParts()
    {
        int partsAffected = 0;
        int partsDetached = 0;

        Debug.Log("=== APPLYING RAW DAMAGE TO ALL PARTS ===");

        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (robotBodyParts[i] == null)
            {
                Debug.LogWarning($"Part at index {i} is null - skipping");
                continue;
            }

            if (partIsDetached[i])
            {
                Debug.Log($"Part '{robotBodyParts[i].name}' already detached - skipping");
                continue;
            }

            float rawDamage = Random.Range(25f, 30f);

            float previousHealth = bodyPartCurrentHealth[i];
            bodyPartCurrentHealth[i] = Mathf.Max(0, bodyPartCurrentHealth[i] - rawDamage);
            float actualDamage = previousHealth - bodyPartCurrentHealth[i];

            lastDamageTime[i] = Time.time;
            partsAffected++;

            Debug.Log($"[{i}] {robotBodyParts[i].name}: {previousHealth:F1} → {bodyPartCurrentHealth[i]:F1} HP (damage: {actualDamage:F1})");

            SpawnDamageEffects(robotBodyParts[i], actualDamage);

            if (bodyPartCurrentHealth[i] <= 0)
            {
                DetachBodyPart(i, Vector3.down * 2f);
                partsDetached++;
            }
        }

        Debug.Log($"=== RAW DAMAGE COMPLETE ===");
        Debug.Log($"Parts affected: {partsAffected}");
        Debug.Log($"Parts detached: {partsDetached}");
    }

    [ContextMenu("Reset Health System")]
    public void ResetHealthSystem()
    {
        Debug.Log("Resetting Robot Health System...");

        StopParry();

        if (robotAnimator != null)
        {
            robotAnimator.enabled = true;

            for (int layerIndex = 0; layerIndex < robotAnimator.layerCount; layerIndex++)
            {
                robotAnimator.SetLayerWeight(layerIndex, 1f);
            }

            Debug.Log("Animator re-enabled and all layer weights reset to 1");
        }

        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (robotBodyParts[i] != null)
            {
                Rigidbody rb = robotBodyParts[i].GetComponent<Rigidbody>();
                if (rb != null && partIsDetached[i])
                {
                    DestroyImmediate(rb);
                    Debug.Log($"Removed Rigidbody from '{robotBodyParts[i].name}'");
                }

                partIsDetached[i] = false;

                if (i < bodyPartMaxHealth.Count)
                {
                    bodyPartCurrentHealth[i] = bodyPartMaxHealth[i];
                }

                lastDamageTime[i] = 0f;

                if (robotBodyParts[i].name == "Head")
                {
                    Vector3 currentRotation = robotBodyParts[i].transform.localEulerAngles;
                    robotBodyParts[i].transform.localEulerAngles = new Vector3(currentRotation.x, 90f, currentRotation.z);
                    Debug.Log($"Reset Head rotation to Y=90°");
                }
            }
        }

        FindMesh0001Part();

        Debug.Log("Health system reset complete!");
    }

    private void DetachBodyPart(int partIndex, Vector3 detachmentForce)
    {
        if (partIndex < 0 || partIndex >= robotBodyParts.Count)
        {
            Debug.LogError($"Invalid part index: {partIndex}");
            return;
        }

        GameObject partToDetach = robotBodyParts[partIndex];
        if (partToDetach == null)
        {
            Debug.LogWarning($"Part at index {partIndex} is null!");
            return;
        }

        if (partIsDetached[partIndex])
        {
            Debug.Log($"Part '{partToDetach.name}' is already detached");
            return;
        }

        Debug.Log($"=== DETACHING BODY PART ===");
        Debug.Log($"Part: {partToDetach.name}");
        Debug.Log($"Health depleted: {bodyPartCurrentHealth[partIndex]:F1}");

        partToDetach.transform.SetParent(null);

        if (partToDetach.name == "Mesh_0.001")
        {
            Debug.Log("Special case: Mesh_0.001 destroyed → Detaching ALL parts, removing NavMeshAgent, and disabling Animator!");

            Transform ultimateParent = partToDetach.transform;
            while (ultimateParent.parent != null)
            {
                ultimateParent = ultimateParent.parent;
            }

            var agent = ultimateParent.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                DestroyImmediate(agent);
                Debug.Log("NavMeshAgent removed from ultimate parent: " + ultimateParent.name);
            }
            else
            {
                Debug.LogWarning("NavMeshAgent not found on ultimate parent: " + ultimateParent.name);
            }

            if (robotAnimator != null)
            {
                robotAnimator.enabled = false;
                Debug.Log("Animator disabled due to Mesh_0.001 destruction");
            }
            else
            {
                Debug.LogWarning("Robot Animator reference is null - cannot disable animator");
            }

            for (int i = 0; i < robotBodyParts.Count; i++)
            {
                if (robotBodyParts[i] == null) continue;

                robotBodyParts[i].transform.SetParent(null);
                HandleCollidersOnDetachment(robotBodyParts[i]);

                Rigidbody rbAll = robotBodyParts[i].GetComponent<Rigidbody>();
                if (rbAll == null) rbAll = robotBodyParts[i].AddComponent<Rigidbody>();

                rbAll.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rbAll.mass = detachedPartMass;
                rbAll.isKinematic = false;
                rbAll.useGravity = detachedPartsUseGravity;

                partIsDetached[i] = true;
            }

            OnPartDestroyed?.Invoke(partToDetach);
            return;
        }

        if (partToDetach.name == "Cylinder.010")
        {
            Debug.Log("Special case: Cylinder.010 (left arm) destroyed → Setting Arm(left) layer weight to 0");

            if (robotAnimator != null)
            {
                int leftArmLayerIndex = robotAnimator.GetLayerIndex("Arm(left)");
                if (leftArmLayerIndex != -1)
                {
                    robotAnimator.SetLayerWeight(leftArmLayerIndex, 0f);
                    Debug.Log($"Set 'Arm(left)' layer weight to 0 (layer index: {leftArmLayerIndex})");
                }
                else
                {
                    Debug.LogWarning("'Arm(left)' layer not found in Animator!");
                }
            }
            else
            {
                Debug.LogWarning("Robot Animator reference is null - cannot set layer weight");
            }
        }

        if (partToDetach.name == "Head")
        {
            Debug.Log("Special case: Head destroyed → Disabling NavMeshAgent on top-most parent");

            Transform topParent = partToDetach.transform;
            while (topParent.parent != null)
            {
                topParent = topParent.parent;
            }

            var agent = topParent.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
                Debug.Log("NavMeshAgent disabled on " + topParent.name);
            }
        }

        HandleCollidersOnDetachment(partToDetach);

        Rigidbody rb = partToDetach.GetComponent<Rigidbody>();
        if (rb == null) rb = partToDetach.AddComponent<Rigidbody>();

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.mass = detachedPartMass;
        rb.isKinematic = false;
        rb.useGravity = detachedPartsUseGravity;

        if (detachmentForce.magnitude > 0.1f)
        {
            Vector3 finalForce = detachmentForce * detachmentForceMultiplier;
            rb.AddForce(finalForce, ForceMode.Impulse);
            Debug.Log($"Applied detachment force: {finalForce.magnitude:F1}N");
        }

        partIsDetached[partIndex] = true;
        SpawnDestructionEffects(partToDetach);
        OnPartDestroyed?.Invoke(partToDetach);

        Debug.Log($"Body part '{partToDetach.name}' successfully detached!");
    }

    private void HandleCollidersOnDetachment(GameObject part)
    {
        Collider[] existingColliders = part.GetComponents<Collider>();
        foreach (Collider col in existingColliders)
        {
            if (col is MeshCollider)
            {
                Debug.Log($"Removing MeshCollider from detached part '{part.name}'");
                DestroyImmediate(col);
            }
            else
            {
                Debug.Log($"Keeping existing {col.GetType().Name} on detached part '{part.name}'");
            }
        }
    }

    private void HandleHealthRegeneration()
    {
        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (partIsDetached[i] || bodyPartCurrentHealth[i] >= bodyPartMaxHealth[i])
                continue;

            if (Time.time - lastDamageTime[i] >= regenDelay)
            {
                float regenAmount = healthRegenRate * Time.deltaTime;
                bodyPartCurrentHealth[i] = Mathf.Min(bodyPartMaxHealth[i], bodyPartCurrentHealth[i] + regenAmount);
            }
        }
    }

    private void SpawnDamageEffects(GameObject damagedPart, float damageAmount)
    {
        if (damageParticleEffect != null)
        {
            GameObject effect = Instantiate(damageParticleEffect, damagedPart.transform.position, Quaternion.identity);
            Destroy(effect, 2f);
        }

        if (audioSource != null && damageSound != null)
        {
            audioSource.PlayOneShot(damageSound, Mathf.Clamp01(damageAmount / 50f));
        }
    }

    private void SpawnDestructionEffects(GameObject destroyedPart)
    {
        if (destructionParticleEffect != null)
        {
            GameObject effect = Instantiate(destructionParticleEffect, destroyedPart.transform.position, Quaternion.identity);
            Destroy(effect, 3f);
        }

        if (audioSource != null && destructionSound != null)
        {
            audioSource.PlayOneShot(destructionSound, 0.8f);
        }
    }

    public float GetPartHealthPercentage(GameObject part)
    {
        int index = robotBodyParts.IndexOf(part);
        if (index == -1 || index >= bodyPartCurrentHealth.Count || index >= bodyPartMaxHealth.Count)
            return 0f;

        return bodyPartCurrentHealth[index] / bodyPartMaxHealth[index];
    }

    public float GetPartCurrentHealth(GameObject part)
    {
        int index = robotBodyParts.IndexOf(part);
        if (index == -1 || index >= bodyPartCurrentHealth.Count)
            return 0f;

        return bodyPartCurrentHealth[index];
    }

    public bool IsPartDetached(GameObject part)
    {
        int index = robotBodyParts.IndexOf(part);
        if (index == -1 || index >= partIsDetached.Count)
            return false;

        return partIsDetached[index];
    }

    public void RepairPart(GameObject part)
    {
        int index = robotBodyParts.IndexOf(part);
        if (index == -1 || partIsDetached[index])
            return;

        bodyPartCurrentHealth[index] = bodyPartMaxHealth[index];
        Debug.Log($"Repaired part '{part.name}' to full health ({bodyPartMaxHealth[index]:F1})");
    }

    public void RepairAllParts()
    {
        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (!partIsDetached[i] && i < bodyPartMaxHealth.Count)
            {
                bodyPartCurrentHealth[i] = bodyPartMaxHealth[i];
            }
        }
        Debug.Log("All non-detached parts repaired to full health");
    }

    [ContextMenu("Log Health Status")]
    public void LogHealthStatus()
    {
        Debug.Log("=== ROBOT HEALTH STATUS ===");
        for (int i = 0; i < robotBodyParts.Count; i++)
        {
            if (robotBodyParts[i] != null)
            {
                string status = partIsDetached[i] ? "DETACHED" : "ATTACHED";
                float currentHP = i < bodyPartCurrentHealth.Count ? bodyPartCurrentHealth[i] : 0f;
                float maxHP = i < bodyPartMaxHealth.Count ? bodyPartMaxHealth[i] : 0f;

                Debug.Log($"[{i}] {robotBodyParts[i].name}: {currentHP:F1}/{maxHP:F1} HP ({status})");
            }
        }
        Debug.Log($"Parry Status: {(isParrying ? "ACTIVE" : "INACTIVE")}");
    }
}