using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OperatorTendrils : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The Operator's main particle system")]
    public ParticleSystem operatorParticleSystem;
    [Tooltip("Reference to the Operator's mana system")]
    public ManaSystem_Operator operatorMana;
    [Tooltip("The player transform")]
    public Transform playerTransform;
    [Tooltip("Reference to the player's mana system for draining")]
    public AdvanceManaSystem playerMana;
    [Tooltip("The Head transform �Eproximity and hit detection measured from here")]
    public Transform headTransform;
    [Tooltip("Player's movement script for knockback")]
    public BasicMovement_2 playerMovement;
    [Tooltip("Player's health script")]
    public PlayerHealth playerHealth;
    [Tooltip("Global Volume for post processing effects")]
    public Volume globalVolume;

    [Header("Proximity Trigger")]
    [Tooltip("Distance from Head at which the tendril attack triggers")]
    public float triggerRadius = 2000f;

    [Header("Mana Cost")]
    [Tooltip("IUMR cost to fire the tendril attack")]
    public float iumrCost = 80f;

    [Header("Particle Noise")]
    [Tooltip("Default noise strength of the particle system")]
    public float defaultNoiseStrength = 124f;
    [Tooltip("Peak noise strength during the attack")]
    public float peakNoiseStrength = 5000f;
    [Tooltip("Time in seconds to ramp up to peak noise")]
    public float expandDuration = 0.2f;

    [Header("Attack Hit Distance")]
    [Tooltip("Distance from Head within which the player gets hit at peak expansion")]
    public float attackDistance = 1500f;

    [Header("Attack Effects on Player")]
    [Tooltip("Health damage dealt to player on hit")]
    public float damageToPlayer = 50f;
    [Tooltip("Total mana reserves drained from player on hit")]
    public float totalManaDrain = 100f;
    [Tooltip("Knockback speed added away from the Operator (velocity, not force)")]
    public float knockbackSpeed = 1000f;

    [Header("Post Exposure Flash")]
    [Tooltip("Instant post exposure value on hit")]
    public float flashExposure = -3f;
    [Tooltip("Default post exposure to fade back to")]
    public float defaultExposure = 1f;
    [Tooltip("Duration to fade back to default exposure")]
    public float exposureFadeDuration = 1f;

    private bool isAttacking = false;
    private bool isBlocked = false;
    private bool isCompressionLocked = false;
    private Coroutine attackCoroutine = null;
    private Coroutine unblockCoroutine = null;
    private ParticleSystem.NoiseModule noiseModule;
    private ColorAdjustments colorAdjustments;

    public bool IsBlocked => isBlocked || isCompressionLocked;

    private void Awake()
    {
        if (operatorParticleSystem != null)
            noiseModule = operatorParticleSystem.noise;

        if (globalVolume != null)
            globalVolume.profile.TryGet(out colorAdjustments);
    }

    private void Update()
    {
        if (isAttacking || IsBlocked) return;
        if (playerTransform == null || headTransform == null) return;

        float distToPlayer = Vector3.Distance(headTransform.position, playerTransform.position);

        if (distToPlayer <= triggerRadius)
        {
            if (operatorMana != null && operatorMana.CurrentIUMR >= iumrCost)
            {
                attackCoroutine = StartCoroutine(TendrilAttack());
            }
        }
    }

    public void BlockAttacks()
    {
        isBlocked = true;

        if (unblockCoroutine != null)
        {
            StopCoroutine(unblockCoroutine);
            unblockCoroutine = null;
        }

        CancelAttack();
    }

    public void UnblockAfter(float delay)
    {
        if (unblockCoroutine != null)
        {
            StopCoroutine(unblockCoroutine);
            unblockCoroutine = null;
        }

        if (delay <= 0f || !gameObject.activeInHierarchy)
        {
            isBlocked = false;
            return;
        }

        unblockCoroutine = StartCoroutine(UnblockRoutine(delay));
    }

    public void SetCompressionLock(bool locked)
    {
        isCompressionLocked = locked;
        if (locked) CancelAttack();
    }

    public void CancelAttack()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (isAttacking)
        {
            isAttacking = false;
            ResetNoise();
        }
    }

    private IEnumerator UnblockRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        isBlocked = false;
        unblockCoroutine = null;
    }

    private void ResetNoise()
    {
        if (operatorParticleSystem == null) return;

        noiseModule.strengthX = defaultNoiseStrength;
        noiseModule.strengthY = defaultNoiseStrength;
        noiseModule.strengthZ = defaultNoiseStrength;
    }

    private IEnumerator TendrilAttack()
    {
        isAttacking = true;

        operatorMana.DrainIUMR(iumrCost);

        float elapsed = 0f;
        while (elapsed < expandDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / expandDuration);
            noiseModule.strengthX = Mathf.Lerp(defaultNoiseStrength, peakNoiseStrength, t);
            noiseModule.strengthY = Mathf.Lerp(defaultNoiseStrength, peakNoiseStrength, t);
            noiseModule.strengthZ = Mathf.Lerp(defaultNoiseStrength, peakNoiseStrength, t);
            yield return null;
        }

        float distAtPeak = Vector3.Distance(headTransform.position, playerTransform.position);
        if (distAtPeak <= attackDistance)
        {
            HitPlayer();
        }

        noiseModule.strengthX = defaultNoiseStrength;
        noiseModule.strengthY = defaultNoiseStrength;
        noiseModule.strengthZ = defaultNoiseStrength;

        isAttacking = false;
        attackCoroutine = null;
    }

    private void HitPlayer()
    {
        if (playerHealth != null)
            playerHealth.TakeDamage(damageToPlayer);

        if (playerMana != null)
            playerMana.AddManaToReserves(-totalManaDrain);

        if (playerMovement != null && headTransform != null)
        {
            Vector3 knockbackDir = (playerTransform.position - headTransform.position).normalized;
            Vector3 current = playerMovement.GetCurrentVelocity();

            float towardOp = Vector3.Dot(current, -knockbackDir);
            if (towardOp > 0f)
                current += knockbackDir * towardOp;

            playerMovement.SetVelocity(current + knockbackDir * knockbackSpeed);
        }

        if (colorAdjustments != null)
        {
            StopCoroutine(nameof(FadeExposure));
            StartCoroutine(nameof(FadeExposure));
        }
    }

    private IEnumerator FadeExposure()
    {
        colorAdjustments.postExposure.value = flashExposure;

        float elapsed = 0f;
        while (elapsed < exposureFadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / exposureFadeDuration);
            colorAdjustments.postExposure.value = Mathf.Lerp(flashExposure, defaultExposure, t);
            yield return null;
        }

        colorAdjustments.postExposure.value = defaultExposure;
    }

    private void OnDrawGizmosSelected()
    {
        if (headTransform == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(headTransform.position, triggerRadius);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(headTransform.position, attackDistance);
    }
}