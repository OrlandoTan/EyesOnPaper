using System;
using UnityEngine;

/// <summary>
/// Step 4: 0-100 suspicion. Rises from what the player does, falls while they
/// behave. Drives the behaviour tiers later, and fires GameEvents.Caught() at 100.
///
/// Every number here is from SCOPE.md §5 — tune them in the Inspector, not in code.
/// </summary>
public class SuspicionMeter : MonoBehaviour
{
    public enum Tier { Low, Medium, High }

    [Header("Refs")]
    [SerializeField] private VisionCone vision;

    [Header("Tier thresholds (%)")]
    [SerializeField] private float mediumAt = 40f;
    [SerializeField] private float highAt = 75f;

    [Header("Gain / decay (% per second)")]
    [Tooltip("Phone visible inside the cone. The fast one.")]
    [SerializeField] private float phoneInConeGain = 40f;
    [Tooltip("Glancing sideways or up at the front, once the grace runs out. The slow one.")]
    [SerializeField] private float lookAwayGain = 4f;
    [SerializeField] private float decay = 5f;

    [Header("Where they're looking")]
    [Tooltip("Degrees off your own paper before you count as not reading it.")]
    [SerializeField] private float sidewaysAngle = 50f;
    [Tooltip("Past this you've turned round in your seat. No grace, and it bites harder.")]
    [SerializeField] private float overShoulderAngle = 100f;
    [Tooltip("Seconds of glancing forward before it starts counting against you.")]
    [SerializeField] private float lookAwayGrace = 2.5f;
    [Tooltip("Caught looking behind you. Applies immediately.")]
    [SerializeField] private float overShoulderGain = 12f;
    [Tooltip("Turned round AND meeting their eye. Locks on instantly.")]
    [SerializeField] private float eyeContactGain = 30f;

    [Header("Proximity")]
    [Tooltip("At or below this distance the gain is at its harshest.")]
    [SerializeField] private float nearDistance = 2f;
    [Tooltip("At or beyond this distance the gain is at its mildest.")]
    [SerializeField] private float farDistance = 12f;
    [SerializeField] private float nearGainMultiplier = 2f;
    [SerializeField] private float farGainMultiplier = 0.6f;

    [Header("Lock-on")]
    [Tooltip("Seconds the invigilator stays fixed on the player after last seeing the phone.")]
    [SerializeField] private float lockDuration = 6f;
    [Tooltip("Decay while locked on. Much slower than normal — being seen has to cost something.")]
    [SerializeField] private float lockedDecay = 1f;

    [Header("Buzz")]
    [Tooltip("Off by default. Jack's timing isn't the player's choice, so punishing it " +
             "is a dice roll — and SCOPE's first pillar says errors must trace back to " +
             "something the player did.")]
    [SerializeField] private bool buzzAlertsInvigilator = false;

    [Header("Buzz spike (flat %), only when the above is on")]
    [SerializeField] private float buzzSpikeNear = 25f;
    [SerializeField] private float buzzSpikeFar = 4f;
    [Tooltip("Inside this distance the buzz is clearly audible.")]
    [SerializeField] private float buzzHearRadius = 6f;

    public float Value { get; private set; }
    public float Value01 => Value / 100f;
    public Tier CurrentTier { get; private set; } = Tier.Low;
    public bool PhoneIsOut { get; private set; }
    public bool IsFrozen { get; private set; }

    /// <summary>True while the invigilator is fixed on the player after catching sight of the phone.</summary>
    public bool IsLocked => lockTimer > 0f;
    public float LockTimeRemaining => Mathf.Max(0f, lockTimer);
    /// <summary>What the meter changed by last frame, per second. For tuning.</summary>
    public float LastRatePerSecond { get; private set; }
    /// <summary>Why it moved last frame. Shown in the debug HUD so a rise is never mysterious.</summary>
    public string LastReason { get; private set; } = "idle";

    /// <summary>Fires only when the tier actually changes, not every frame.</summary>
    public event Action<Tier> OnTierChanged;

    private float lookAwayTimer;
    private float lockTimer;
    private bool caughtFired;

    private void Awake()
    {
        if (vision == null) vision = GetComponentInChildren<VisionCone>();
    }

    private void OnEnable()
    {
        GameEvents.OnPhoneShown  += HandlePhoneShown;
        GameEvents.OnPhoneHidden += HandlePhoneHidden;
        GameEvents.OnBuzz        += HandleBuzz;
        GameEvents.OnCaught      += Freeze;
        GameEvents.OnExamEnded   += Freeze;
    }

    private void OnDisable()
    {
        GameEvents.OnPhoneShown  -= HandlePhoneShown;
        GameEvents.OnPhoneHidden -= HandlePhoneHidden;
        GameEvents.OnBuzz        -= HandleBuzz;
        GameEvents.OnCaught      -= Freeze;
        GameEvents.OnExamEnded   -= Freeze;
    }

    private void Update()
    {
        if (IsFrozen) return;

        lockTimer -= Time.deltaTime;

        float perSecond = IsLocked ? -lockedDecay : -decay;
        bool seen = vision != null && vision.CanSeePlayer;

        if (PhoneIsOut && seen)
        {
            // Caught with it out: latch on, and bite harder the closer they are.
            lockTimer = lockDuration;
            perSecond = phoneInConeGain * ProximityMultiplier();
            lookAwayTimer = 0f;
            LastReason = "PHONE SEEN";
        }
        else if (seen && vision.PlayerIsLookingAtMe && vision.PlayerLookAwayAngle >= overShoulderAngle)
        {
            // Turned round and looking them in the eye. There is no explaining this one.
            lockTimer = lockDuration;
            perSecond = eyeContactGain;
            lookAwayTimer = lookAwayGrace;
            LastReason = "EYE CONTACT";
        }
        else if (seen && vision.PlayerLookAwayAngle >= overShoulderAngle)
        {
            // Turned round in your seat. Nobody does this for an innocent reason.
            perSecond = overShoulderGain;
            lookAwayTimer = lookAwayGrace;   // stays armed, so glancing back costs on sight
            LastReason = $"TURNED AROUND ({vision.PlayerLookAwayAngle:0}deg)";
        }
        else if (seen && vision.PlayerLookAwayAngle >= sidewaysAngle)
        {
            // Looking up at the front is what honest students do — for a while.
            lookAwayTimer += Time.deltaTime;
            if (lookAwayTimer >= lookAwayGrace)
            {
                perSecond = lookAwayGain;
                LastReason = $"STARING OFF ({vision.PlayerLookAwayAngle:0}deg)";
            }
            else
            {
                float left = lookAwayGrace - lookAwayTimer;
                LastReason = $"glancing up, {left:0.0}s of grace left";
            }
        }
        else
        {
            lookAwayTimer = 0f;
            LastReason = IsLocked ? "locked on, slow bleed" : "calm, decaying";
        }

        LastRatePerSecond = perSecond;
        Add(perSecond * Time.deltaTime);
    }

    /// <summary>1 at mid range, up to nearGainMultiplier point blank, down to farGainMultiplier across the room.</summary>
    private float ProximityMultiplier()
    {
        if (vision == null) return 1f;

        float t = Mathf.InverseLerp(nearDistance, farDistance, vision.DistanceToPlayer);
        return Mathf.Lerp(nearGainMultiplier, farGainMultiplier, t);
    }

    /// <summary>Public so one-off events and debug keys can push the meter.</summary>
    public void Add(float amount)
    {
        if (IsFrozen) return;

        Value = Mathf.Clamp(Value + amount, 0f, 100f);
        RefreshTier();

        if (Value >= 100f && !caughtFired)
        {
            caughtFired = true;
            GameEvents.Caught();
        }
    }

    private void RefreshTier()
    {
        Tier next = Value >= highAt   ? Tier.High
                  : Value >= mediumAt ? Tier.Medium
                  : Tier.Low;

        if (next == CurrentTier) return;
        CurrentTier = next;
        OnTierChanged?.Invoke(next);
    }

    private void HandlePhoneShown()  => PhoneIsOut = true;
    private void HandlePhoneHidden() => PhoneIsOut = false;

    private void HandleBuzz()
    {
        if (!buzzAlertsInvigilator) return;

        if (vision == null) { Add(buzzSpikeFar); return; }
        Add(vision.DistanceToPlayer <= buzzHearRadius ? buzzSpikeNear : buzzSpikeFar);
    }

    private void Freeze()
    {
        IsFrozen = true;
        lockTimer = 0f;
    }
}
