
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
// Alias für EnhancedTouch, um Konflikte mit UnityEngine.Touch zu vermeiden
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

public class MovementController : MonoBehaviour
{
    [Header("Kamera & Bewegung")]
    public CinemachineCamera cineCam;

    [Header("Scroll Settings")]
    public Slider scrollSpeedSlider; // Hier den Slider einfügen
    public float scrollSpeedMetersPerPixel;

    private float initialScrollSpeed;

    [Header("Yaw & Look")]
    public float lookSpeed = 0.1f;
    public float rotationDamping = 0.1f;

    [Header("Eingabeoptionen")]
    public bool invertScroll = false;

    [Header("Spline-Ende blockieren")]
    public float blockEndOffset = 0.5f;

    [Header("GUI-Debug (auch im Build)")]
    public bool showDebugGUI = true;

    [Header("SpeedZones (werden automatisch gefunden, falls leer)")]
    public SpeedZone[] speedZones;

    [Header("AnimationZones (werden automatisch gefunden, falls leer)")]
    public AnimationZone[] animationZones;

    [Header("Touch-Einstellungen (Windows Touchscreen)")]
    [Range(1, 5)]
    public int fingersToScroll = 2;

    [Header("Look Modus Switch")]
    [Tooltip("Wenn aktiv: 1 Finger gedrückt halten und dann draggen zum Umsehen. Deaktiviert das 1-Finger-Look aus Option A.")]
    public bool useHoldToLook = false;
    [Tooltip("Wie lange (Sek.) ein Finger gehalten werden muss, bevor Look aktiviert wird (nur Option B).")]
    [Min(0f)] public float holdToLookDelay = 0.25f;

    private CinemachineSplineDolly splineDolly;
    private float scrollVelocity;
    private float currentDistance;

    private YawOverrideExtension yawExtension;
    private float yawAmount;

    private Coroutine scrollSpeedCoroutine;

    private Vector2 previousScrollPos;
    private Vector2 previousLookPos;
    private bool isScrolling;
    private bool isLooking;

    private CinemachineBasicMultiChannelPerlin shakePerlin;

    private bool isClamped = false;
    private SpeedZone activeZone;

    // --- Hold-to-Look (Option B) State ---
    private int holdFingerId = -1;
    private float holdStartTime = 0f;
    private bool holdLookActive = false;

    // ---------------- Lifecycle ----------------

    void Awake()
    {
        // Enhanced Touch schon in Awake aktivieren (sicher für Builds)
        ETouch.EnhancedTouchSupport.Enable();
    }


    public void SetHoldToLook(bool enabled)
    {
        useHoldToLook = enabled;
    }



    void Start()
    {
        if (scrollSpeedSlider != null)
        {
            // Wert direkt vom Slider übernehmen
            scrollSpeedMetersPerPixel = scrollSpeedSlider.value;
            initialScrollSpeed = scrollSpeedMetersPerPixel;

            // Slider bewegt → Wert sofort anpassen
            scrollSpeedSlider.onValueChanged.AddListener(OnSliderChanged);
        }


        splineDolly = cineCam.GetComponent<CinemachineSplineDolly>();
        currentDistance = splineDolly.CameraPosition;

        yawExtension = cineCam.GetComponent<YawOverrideExtension>();
        if (yawExtension == null)
            yawExtension = cineCam.gameObject.AddComponent<YawOverrideExtension>();

        shakePerlin = cineCam.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>();

        initialScrollSpeed = scrollSpeedMetersPerPixel;

        if (speedZones == null || speedZones.Length == 0)
            speedZones = Object.FindObjectsByType<SpeedZone>(FindObjectsSortMode.None);

        if (animationZones == null || animationZones.Length == 0)
            animationZones = Object.FindObjectsByType<AnimationZone>(FindObjectsSortMode.None);

        fingersToScroll = Mathf.Clamp(fingersToScroll, 1, 5);
    }


    void OnDestroy()
    {
        ETouch.EnhancedTouchSupport.Disable();
    }


    void Update()
    {
        float maxDistance = splineDolly.Spline?.Spline?.GetLength() ?? 0f;
        float usableMaxDistance = Mathf.Max(0f, maxDistance - blockEndOffset);

        HandleInput();

        float effectiveVelocity = scrollVelocity;
        currentDistance = Mathf.Clamp(currentDistance + effectiveVelocity * Time.deltaTime, 0, usableMaxDistance);
        splineDolly.CameraPosition = currentDistance;

        // Clamp-Logik
        if ((currentDistance >= usableMaxDistance && scrollVelocity > 0f) ||
            (currentDistance <= 0f && scrollVelocity < 0f))
        {
            isClamped = true;
            scrollVelocity = 0f;
        }
        else
        {
            isClamped = false;
        }

        // Dämpfung der Scrollgeschwindigkeit
        float damping = 0.9f;
        float dampingFactor = Mathf.Pow(damping, Time.deltaTime * 60f);
        scrollVelocity *= dampingFactor;
        if (Mathf.Abs(scrollVelocity) < 0.001f)
            scrollVelocity = 0f;

        if (!isLooking)
            yawAmount = Mathf.Lerp(yawAmount, 0f, Time.deltaTime * 5f);

        if (yawExtension != null)
        {
            yawAmount = Mathf.Clamp(yawAmount, yawExtension.minYawLimit, yawExtension.maxYawLimit);
            yawExtension.yawOverride = yawAmount;
        }

        if (shakePerlin != null)
            shakePerlin.AmplitudeGain = (isScrolling || isLooking) ? 0f : 1f;

        HandleSpeedZones(maxDistance);
        HandleAnimationZones();
    }

    // ---------------- Zones ----------------

    private void HandleSpeedZones(float totalLength)
    {
        SpeedZone matchingZone = null;
        foreach (var zone in speedZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            float start = Mathf.Clamp(zone.startDistance, 0, totalLength);
            float end = Mathf.Clamp(zone.endDistance, start, totalLength);

            if (currentDistance >= start && currentDistance <= end)
            {
                matchingZone = zone;
                break;
            }
        }

        if (matchingZone != null && matchingZone != activeZone)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                matchingZone.newScrollSpeed, matchingZone.easeDuration, matchingZone.ease));
            activeZone = matchingZone;
        }
        else if (matchingZone == null && activeZone != null && activeZone.resetOnExit)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            float resetTo = activeZone.defaultSpeed > 0 ? activeZone.defaultSpeed : initialScrollSpeed;
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                resetTo, activeZone.resetEaseDuration, activeZone.resetEase));
            activeZone = null;
        }
    }

    private void HandleAnimationZones()
    {
        if (animationZones == null) return;
        foreach (var zone in animationZones)
        {
            if (zone == null) continue;
            zone.ScrubAnimation(currentDistance);
        }
    }

    // ---------------- Helpers ----------------

    private IEnumerator ChangeScrollSpeedSmoothly(float newSpeed, float duration, SpeedZone.EaseType ease)
    {
        float startSpeed = scrollSpeedMetersPerPixel;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float easedT = Ease(t, ease);
            scrollSpeedMetersPerPixel = Mathf.Lerp(startSpeed, newSpeed, easedT);
            yield return null;
        }
        scrollSpeedMetersPerPixel = newSpeed;
    }

    private float Ease(float t, SpeedZone.EaseType ease)
    {
        switch (ease)
        {
            case SpeedZone.EaseType.Linear: return t;
            case SpeedZone.EaseType.SmoothStep: return Mathf.SmoothStep(0f, 1f, t);
            case SpeedZone.EaseType.EaseIn: return t * t;
            case SpeedZone.EaseType.EaseOut: return t * (2 - t);
            case SpeedZone.EaseType.EaseInOut: return t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
            case SpeedZone.EaseType.SineWave: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            default: return t;
        }
    }

    Vector2 GetAverageActiveTouchPosition()
    {
        var touches = ETouch.Touch.activeTouches;
        if (touches.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < touches.Count; i++)
            sum += touches[i].screenPosition;
        return sum / touches.Count;
    }

    // ---------------- Input (Windows Touch + Maus) ----------------

    void HandleInput()
    {
        bool hasMouse = Mouse.current != null;
        bool leftPressed = hasMouse && Mouse.current.leftButton.isPressed;
        bool rightPressed = hasMouse && Mouse.current.rightButton.isPressed;

        int activeTouchCount = ETouch.Touch.activeTouches.Count;
        bool touchDevicePresent = Touchscreen.current != null;
        bool touchActive = touchDevicePresent && activeTouchCount > 0;

        // ---- SCROLL (beide Optionen verwenden dasselbe Scroll-Kriterium) ----
        if ((touchActive && activeTouchCount >= fingersToScroll) || (!touchActive && leftPressed))
        {
            Vector2 currentPos = touchActive ? GetAverageActiveTouchPosition() : Mouse.current.position.ReadValue();

            if (!isScrolling)
            {
                previousScrollPos = currentPos;
                isScrolling = true;
            }
            else
            {
                float deltaY = currentPos.y - previousScrollPos.y;
                ApplyScroll(deltaY);
                previousScrollPos = currentPos;
            }
        }
        else
        {
            isScrolling = false;
        }

        // ---- LOOK (Switch A/B) ----
        if (!useHoldToLook)
        {
            // --- Option A (Default): 1 Finger sofort Look (sofern Scroll nicht auf 1 Finger liegt) ---
            bool lookTouchCondition = touchActive && activeTouchCount == 1 && fingersToScroll != 1;

            if ((lookTouchCondition) || (!touchActive && rightPressed))
            {
                Vector2 lookPos = touchActive ? GetAverageActiveTouchPosition() : Mouse.current.position.ReadValue();

                if (!isLooking)
                {
                    previousLookPos = lookPos;
                    isLooking = true;
                }
                else
                {
                    Vector2 delta = lookPos - previousLookPos;
                    ApplyLook(delta);
                    previousLookPos = lookPos;
                }
            }
            else
            {
                isLooking = false;
            }

            // Hold-States sicherheitshalber zurücksetzen
            ResetHoldLookState();
        }
        else
        {
            // --- Option B: Hold-to-Look mit 1 Finger ---
            // Maus: weiterhin Rechte Maustaste
            bool mouseLook = !touchActive && rightPressed;

            if (mouseLook)
            {
                Vector2 lookPos = Mouse.current.position.ReadValue();
                if (!isLooking)
                {
                    previousLookPos = lookPos;
                    isLooking = true;
                }
                else
                {
                    Vector2 delta = lookPos - previousLookPos;
                    ApplyLook(delta);
                    previousLookPos = lookPos;
                }
                // Touch-Hold-States zurücksetzen, wenn Maus genutzt wird
                ResetHoldLookState();
                return;
            }

            // Touch: exakt 1 Finger erforderlich, sonst Hold-Flow abbrechen
            if (touchActive && activeTouchCount == 1)
            {
                var t = ETouch.Touch.activeTouches[0];

                // Start eines möglichen Holds
                if (holdFingerId == -1 && (t.phase == UnityEngine.InputSystem.TouchPhase.Began || t.phase == UnityEngine.InputSystem.TouchPhase.Stationary || t.phase == UnityEngine.InputSystem.TouchPhase.Moved))
                {
                    holdFingerId = t.touchId;
                    holdStartTime = Time.time;
                    holdLookActive = false;
                    previousLookPos = t.screenPosition;
                }
                else if (t.touchId == holdFingerId)
                {
                    // prüfen, ob die Haltezeit erreicht ist
                    if (!holdLookActive && (Time.time - holdStartTime) >= holdToLookDelay)
                    {
                        holdLookActive = true;
                        // Startpunkt für glattes Einsetzen
                        previousLookPos = t.screenPosition;
                    }

                    // Wenn aktiv, dann draggen = Look
                    if (holdLookActive)
                    {
                        Vector2 lookPos = t.screenPosition;
                        if (!isLooking)
                        {
                            previousLookPos = lookPos;
                            isLooking = true;
                        }
                        else
                        {
                            Vector2 delta = lookPos - previousLookPos;
                            ApplyLook(delta);
                            previousLookPos = lookPos;
                        }
                    }
                    else
                    {
                        // Noch im Hold-Zeitfenster: kein Look
                        isLooking = false;
                    }
                }

                // Finger losgelassen?
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Canceled || t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    ResetHoldLookState();
                    isLooking = false;
                }
            }
            else
            {
                // Kein oder mehrere Finger: Hold-Flow abbrechen
                ResetHoldLookState();
                isLooking = false;
            }
        }
    }

    void ResetHoldLookState()
    {
        holdFingerId = -1;
        holdStartTime = 0f;
        holdLookActive = false;
    }

    // ---------------- Movement ----------------

    void ApplyScroll(float deltaY)
    {
        float direction = invertScroll ? -1f : 1f;
        scrollVelocity += deltaY * scrollSpeedMetersPerPixel * direction;
    }

    void ApplyLook(Vector2 delta)
    {
        float rawYawDelta = delta.x * lookSpeed;
        float nonLinearDamping = Mathf.Pow(Mathf.Abs(yawAmount), 1.2f);
        float dampFactor = 1f / (1f + nonLinearDamping * rotationDamping);
        float adjustedYawDelta = rawYawDelta * dampFactor;
        yawAmount += adjustedYawDelta;
    }


    // ---------------- Slider Integration ----------------

    public void ApplySliderSettings()
    {
        if (scrollSpeedSlider != null)
        {
            scrollSpeedMetersPerPixel = scrollSpeedSlider.value;
            initialScrollSpeed = scrollSpeedMetersPerPixel; // 🔹 hier setzen, damit Reset nicht überschreibt
            PlayerPrefs.SetFloat("ScrollSpeed", scrollSpeedMetersPerPixel);
            PlayerPrefs.Save();
            Debug.Log($"[MovementController] ScrollSpeed gespeichert: {scrollSpeedMetersPerPixel}");
        }
    }

    private void OnSliderChanged(float value)
    {
        scrollSpeedMetersPerPixel = value;
        initialScrollSpeed = value; // damit SpeedZones nicht zurücksetzen
        PlayerPrefs.SetFloat("ScrollSpeed", value); // optional speichern
        PlayerPrefs.Save();
        Debug.Log($"[MovementController] ScrollSpeed geändert: {value}");
    }






    // ---------------- Debug Overlay ----------------

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            normal = { textColor = Color.white }
        };

        float y = 10f;
        float line = 22f;

        GUI.Label(new Rect(10, y + 0 * line, 1000, line), $"Option: {(useHoldToLook ? "B (Hold-to-Look)" : "A (Sofort-Look)")}", style);
        GUI.Label(new Rect(10, y + 1 * line, 1000, line), $"Active Touches: {ETouch.Touch.activeTouches.Count}", style);
        GUI.Label(new Rect(10, y + 2 * line, 1000, line), $"Hold Active: {holdLookActive}", style);
        GUI.Label(new Rect(10, y + 3 * line, 1000, line), $"Scroll Velocity: {scrollVelocity:F2}", style);
        GUI.Label(new Rect(10, y + 4 * line, 1000, line), $"Scroll Speed: {scrollSpeedMetersPerPixel:F4}", style);
        GUI.Label(new Rect(10, y + 5 * line, 1000, line), $"Distance: {currentDistance:F2}", style);
        GUI.Label(new Rect(10, y + 6 * line, 1000, line), $"Yaw: {yawExtension?.yawOverride:F2}", style);
        GUI.Label(new Rect(10, y + 7 * line, 1000, line), $"Clamped: {isClamped}", style);
        GUI.Label(new Rect(10, y + 8 * line, 1000, line), $"SpeedZone: {(activeZone ? activeZone.name : "keine")}", style);
    }

}





/*
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
// Alias für EnhancedTouch, um Konflikte mit UnityEngine.Touch zu vermeiden
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

public class MovementController : MonoBehaviour
{
    [Header("Kamera & Bewegung")]
    public CinemachineCamera cineCam;
    public float scrollSpeedMetersPerPixel = 0.01f;

    [Header("Yaw & Look")]
    public float lookSpeed = 0.1f;
    public float rotationDamping = 0.1f;

    [Header("Eingabeoptionen")]
    public bool invertScroll = false;

    [Header("Spline-Ende blockieren")]
    public float blockEndOffset = 0.5f;

    [Header("GUI-Debug (auch im Build)")]
    public bool showDebugGUI = true;

    [Header("SpeedZones (werden automatisch gefunden, falls leer)")]
    public SpeedZone[] speedZones;

    [Header("AnimationZones (werden automatisch gefunden, falls leer)")]
    public AnimationZone[] animationZones;

    [Header("Touch-Einstellungen (Windows Touchscreen)")]
    [Range(1, 5)]
    public int fingersToScroll = 2;

    [Header("Look Modus Switch")]
    [Tooltip("Wenn aktiv: 1 Finger gedrückt halten und dann draggen zum Umsehen. Deaktiviert das 1-Finger-Look aus Option A.")]
    public bool useHoldToLook = false;
    [Tooltip("Wie lange (Sek.) ein Finger gehalten werden muss, bevor Look aktiviert wird (nur Option B).")]
    [Min(0f)] public float holdToLookDelay = 0.25f;

    private CinemachineSplineDolly splineDolly;
    private float scrollVelocity;
    private float currentDistance;

    private YawOverrideExtension yawExtension;
    private float yawAmount;

    private Coroutine scrollSpeedCoroutine;

    private Vector2 previousScrollPos;
    private Vector2 previousLookPos;
    private bool isScrolling;
    private bool isLooking;

    private CinemachineBasicMultiChannelPerlin shakePerlin;

    private bool isClamped = false;
    private SpeedZone activeZone;
    private float initialScrollSpeed;

    // --- Hold-to-Look (Option B) State ---
    private int holdFingerId = -1;
    private float holdStartTime = 0f;
    private bool holdLookActive = false;

    // ---------------- Lifecycle ----------------

    void Awake()
    {
        // Enhanced Touch schon in Awake aktivieren (sicher für Builds)
        ETouch.EnhancedTouchSupport.Enable();
    }


    public void SetHoldToLook(bool enabled)
    {
        useHoldToLook = enabled;
    }



    void Start()
    {
        splineDolly = cineCam.GetComponent<CinemachineSplineDolly>();
        currentDistance = splineDolly.CameraPosition;

        yawExtension = cineCam.GetComponent<YawOverrideExtension>();
        if (yawExtension == null)
            yawExtension = cineCam.gameObject.AddComponent<YawOverrideExtension>();

        shakePerlin = cineCam.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>();

        initialScrollSpeed = scrollSpeedMetersPerPixel;

        if (speedZones == null || speedZones.Length == 0)
            speedZones = Object.FindObjectsByType<SpeedZone>(FindObjectsSortMode.None);

        if (animationZones == null || animationZones.Length == 0)
            animationZones = Object.FindObjectsByType<AnimationZone>(FindObjectsSortMode.None);

        fingersToScroll = Mathf.Clamp(fingersToScroll, 1, 5);
    }
    

    void OnDestroy()
    {
        ETouch.EnhancedTouchSupport.Disable();
    }


    void Update()
    {
        float maxDistance = splineDolly.Spline?.Spline?.GetLength() ?? 0f;
        float usableMaxDistance = Mathf.Max(0f, maxDistance - blockEndOffset);

        HandleInput();

        float effectiveVelocity = scrollVelocity;
        currentDistance = Mathf.Clamp(currentDistance + effectiveVelocity * Time.deltaTime, 0, usableMaxDistance);
        splineDolly.CameraPosition = currentDistance;

        // Clamp-Logik
        if ((currentDistance >= usableMaxDistance && scrollVelocity > 0f) ||
            (currentDistance <= 0f && scrollVelocity < 0f))
        {
            isClamped = true;
            scrollVelocity = 0f;
        }
        else
        {
            isClamped = false;
        }

        // Dämpfung der Scrollgeschwindigkeit
        float damping = 0.9f;
        float dampingFactor = Mathf.Pow(damping, Time.deltaTime * 60f);
        scrollVelocity *= dampingFactor;
        if (Mathf.Abs(scrollVelocity) < 0.001f)
            scrollVelocity = 0f;

        if (!isLooking)
            yawAmount = Mathf.Lerp(yawAmount, 0f, Time.deltaTime * 5f);

        if (yawExtension != null)
        {
            yawAmount = Mathf.Clamp(yawAmount, yawExtension.minYawLimit, yawExtension.maxYawLimit);
            yawExtension.yawOverride = yawAmount;
        }

        if (shakePerlin != null)
            shakePerlin.AmplitudeGain = (isScrolling || isLooking) ? 0f : 1f;

        HandleSpeedZones(maxDistance);
        HandleAnimationZones();
    }

    // ---------------- Zones ----------------

    private void HandleSpeedZones(float totalLength)
    {
        SpeedZone matchingZone = null;
        foreach (var zone in speedZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            float start = Mathf.Clamp(zone.startDistance, 0, totalLength);
            float end = Mathf.Clamp(zone.endDistance, start, totalLength);

            if (currentDistance >= start && currentDistance <= end)
            {
                matchingZone = zone;
                break;
            }
        }

        if (matchingZone != null && matchingZone != activeZone)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                matchingZone.newScrollSpeed, matchingZone.easeDuration, matchingZone.ease));
            activeZone = matchingZone;
        }
        else if (matchingZone == null && activeZone != null && activeZone.resetOnExit)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            float resetTo = activeZone.defaultSpeed > 0 ? activeZone.defaultSpeed : initialScrollSpeed;
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                resetTo, activeZone.resetEaseDuration, activeZone.resetEase));
            activeZone = null;
        }
    }

    private void HandleAnimationZones()
    {
        if (animationZones == null) return;
        foreach (var zone in animationZones)
        {
            if (zone == null) continue;
            zone.ScrubAnimation(currentDistance);
        }
    }

    // ---------------- Helpers ----------------

    private IEnumerator ChangeScrollSpeedSmoothly(float newSpeed, float duration, SpeedZone.EaseType ease)
    {
        float startSpeed = scrollSpeedMetersPerPixel;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float easedT = Ease(t, ease);
            scrollSpeedMetersPerPixel = Mathf.Lerp(startSpeed, newSpeed, easedT);
            yield return null;
        }
        scrollSpeedMetersPerPixel = newSpeed;
    }

    private float Ease(float t, SpeedZone.EaseType ease)
    {
        switch (ease)
        {
            case SpeedZone.EaseType.Linear: return t;
            case SpeedZone.EaseType.SmoothStep: return Mathf.SmoothStep(0f, 1f, t);
            case SpeedZone.EaseType.EaseIn: return t * t;
            case SpeedZone.EaseType.EaseOut: return t * (2 - t);
            case SpeedZone.EaseType.EaseInOut: return t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
            case SpeedZone.EaseType.SineWave: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            default: return t;
        }
    }

    Vector2 GetAverageActiveTouchPosition()
    {
        var touches = ETouch.Touch.activeTouches;
        if (touches.Count == 0) return Vector2.zero;
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < touches.Count; i++)
            sum += touches[i].screenPosition;
        return sum / touches.Count;
    }

    // ---------------- Input (Windows Touch + Maus) ----------------

    void HandleInput()
    {
        bool hasMouse = Mouse.current != null;
        bool leftPressed = hasMouse && Mouse.current.leftButton.isPressed;
        bool rightPressed = hasMouse && Mouse.current.rightButton.isPressed;

        int activeTouchCount = ETouch.Touch.activeTouches.Count;
        bool touchDevicePresent = Touchscreen.current != null;
        bool touchActive = touchDevicePresent && activeTouchCount > 0;

        // ---- SCROLL (beide Optionen verwenden dasselbe Scroll-Kriterium) ----
        if ((touchActive && activeTouchCount >= fingersToScroll) || (!touchActive && leftPressed))
        {
            Vector2 currentPos = touchActive ? GetAverageActiveTouchPosition() : Mouse.current.position.ReadValue();

            if (!isScrolling)
            {
                previousScrollPos = currentPos;
                isScrolling = true;
            }
            else
            {
                float deltaY = currentPos.y - previousScrollPos.y;
                ApplyScroll(deltaY);
                previousScrollPos = currentPos;
            }
        }
        else
        {
            isScrolling = false;
        }

        // ---- LOOK (Switch A/B) ----
        if (!useHoldToLook)
        {
            // --- Option A (Default): 1 Finger sofort Look (sofern Scroll nicht auf 1 Finger liegt) ---
            bool lookTouchCondition = touchActive && activeTouchCount == 1 && fingersToScroll != 1;

            if ((lookTouchCondition) || (!touchActive && rightPressed))
            {
                Vector2 lookPos = touchActive ? GetAverageActiveTouchPosition() : Mouse.current.position.ReadValue();

                if (!isLooking)
                {
                    previousLookPos = lookPos;
                    isLooking = true;
                }
                else
                {
                    Vector2 delta = lookPos - previousLookPos;
                    ApplyLook(delta);
                    previousLookPos = lookPos;
                }
            }
            else
            {
                isLooking = false;
            }

            // Hold-States sicherheitshalber zurücksetzen
            ResetHoldLookState();
        }
        else
        {
            // --- Option B: Hold-to-Look mit 1 Finger ---
            // Maus: weiterhin Rechte Maustaste
            bool mouseLook = !touchActive && rightPressed;

            if (mouseLook)
            {
                Vector2 lookPos = Mouse.current.position.ReadValue();
                if (!isLooking)
                {
                    previousLookPos = lookPos;
                    isLooking = true;
                }
                else
                {
                    Vector2 delta = lookPos - previousLookPos;
                    ApplyLook(delta);
                    previousLookPos = lookPos;
                }
                // Touch-Hold-States zurücksetzen, wenn Maus genutzt wird
                ResetHoldLookState();
                return;
            }

            // Touch: exakt 1 Finger erforderlich, sonst Hold-Flow abbrechen
            if (touchActive && activeTouchCount == 1)
            {
                var t = ETouch.Touch.activeTouches[0];

                // Start eines möglichen Holds
                if (holdFingerId == -1 && (t.phase == UnityEngine.InputSystem.TouchPhase.Began || t.phase == UnityEngine.InputSystem.TouchPhase.Stationary || t.phase == UnityEngine.InputSystem.TouchPhase.Moved))
                {
                    holdFingerId = t.touchId;
                    holdStartTime = Time.time;
                    holdLookActive = false;
                    previousLookPos = t.screenPosition;
                }
                else if (t.touchId == holdFingerId)
                {
                    // prüfen, ob die Haltezeit erreicht ist
                    if (!holdLookActive && (Time.time - holdStartTime) >= holdToLookDelay)
                    {
                        holdLookActive = true;
                        // Startpunkt für glattes Einsetzen
                        previousLookPos = t.screenPosition;
                    }

                    // Wenn aktiv, dann draggen = Look
                    if (holdLookActive)
                    {
                        Vector2 lookPos = t.screenPosition;
                        if (!isLooking)
                        {
                            previousLookPos = lookPos;
                            isLooking = true;
                        }
                        else
                        {
                            Vector2 delta = lookPos - previousLookPos;
                            ApplyLook(delta);
                            previousLookPos = lookPos;
                        }
                    }
                    else
                    {
                        // Noch im Hold-Zeitfenster: kein Look
                        isLooking = false;
                    }
                }

                // Finger losgelassen?
                if (t.phase == UnityEngine.InputSystem.TouchPhase.Canceled || t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    ResetHoldLookState();
                    isLooking = false;
                }
            }
            else
            {
                // Kein oder mehrere Finger: Hold-Flow abbrechen
                ResetHoldLookState();
                isLooking = false;
            }
        }
    }

    void ResetHoldLookState()
    {
        holdFingerId = -1;
        holdStartTime = 0f;
        holdLookActive = false;
    }

    // ---------------- Movement ----------------

    void ApplyScroll(float deltaY)
    {
        float direction = invertScroll ? -1f : 1f;
        scrollVelocity += deltaY * scrollSpeedMetersPerPixel * direction;
    }

    void ApplyLook(Vector2 delta)
    {
        float rawYawDelta = delta.x * lookSpeed;
        float nonLinearDamping = Mathf.Pow(Mathf.Abs(yawAmount), 1.2f);
        float dampFactor = 1f / (1f + nonLinearDamping * rotationDamping);
        float adjustedYawDelta = rawYawDelta * dampFactor;
        yawAmount += adjustedYawDelta;
    }


    // ---------------- Slider Integration ----------------

    public void ApplySliderSettings(float newScrollSpeed)
    {
        scrollSpeedMetersPerPixel = newScrollSpeed;
        initialScrollSpeed = newScrollSpeed; // 👈 verhindert Zurücksetzen
        Debug.Log($"[MovementController] Neuer Scroll Speed übernommen: {scrollSpeedMetersPerPixel}");
    }




    // ---------------- Debug Overlay ----------------

    void OnGUI()
    {
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            normal = { textColor = Color.white }
        };

        float y = 10f;
        float line = 22f;

        GUI.Label(new Rect(10, y + 0 * line, 1000, line), $"Option: {(useHoldToLook ? "B (Hold-to-Look)" : "A (Sofort-Look)")}", style);
        GUI.Label(new Rect(10, y + 1 * line, 1000, line), $"Active Touches: {ETouch.Touch.activeTouches.Count}", style);
        GUI.Label(new Rect(10, y + 2 * line, 1000, line), $"Hold Active: {holdLookActive}", style);
        GUI.Label(new Rect(10, y + 3 * line, 1000, line), $"Scroll Velocity: {scrollVelocity:F2}", style);
        GUI.Label(new Rect(10, y + 4 * line, 1000, line), $"Scroll Speed: {scrollSpeedMetersPerPixel:F4}", style);
        GUI.Label(new Rect(10, y + 5 * line, 1000, line), $"Distance: {currentDistance:F2}", style);
        GUI.Label(new Rect(10, y + 6 * line, 1000, line), $"Yaw: {yawExtension?.yawOverride:F2}", style);
        GUI.Label(new Rect(10, y + 7 * line, 1000, line), $"Clamped: {isClamped}", style);
        GUI.Label(new Rect(10, y + 8 * line, 1000, line), $"SpeedZone: {(activeZone ? activeZone.name : "keine")}", style);
    }

}
*/







/*
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
// Optional: Wenn du EnhancedTouch nicht brauchst, kannst du die nächste Zeile und die Enable/Disable-Aufrufe entfernen.
// using UnityEngine.InputSystem.EnhancedTouch;
using Unity.Cinemachine;

public class MovementController : MonoBehaviour
{
    [Header("Kamera & Bewegung")]
    public CinemachineCamera cineCam;
    public float scrollSpeedMetersPerPixel = 0.01f;

    [Header("Yaw & Look")]
    public float lookSpeed = 0.1f;
    public float rotationDamping = 0.1f;

    [Header("Eingabeoptionen")]
    public bool invertScroll = false;

    [Header("Spline-Ende blockieren")]
    public float blockEndOffset = 0.5f;

    [Header("GUI-Debug")]
    public bool showDebugGUI = true;

    [Header("SpeedZones (werden automatisch gefunden, falls leer)")]
    public SpeedZone[] speedZones;

    [Header("AnimationZones (werden automatisch gefunden, falls leer)")]
    public AnimationZone[] animationZones;

    [Header("Touch-Einstellungen")]
    [Range(1, 5)]
    public int fingersToScroll = 2;

    private CinemachineSplineDolly splineDolly;
    private float scrollVelocity;
    private float currentDistance;

    private YawOverrideExtension yawExtension;
    private float yawAmount;

    private Coroutine scrollSpeedCoroutine;

    private Vector2 previousScrollPos;
    private Vector2 previousLookPos;
    private bool isScrolling;
    private bool isLooking;

    private CinemachineBasicMultiChannelPerlin shakePerlin;

    private bool isClamped = false;
    private SpeedZone activeZone;
    private float initialScrollSpeed;

    void Start()
    {
        // Optional:
        // EnhancedTouchSupport.Enable();

        splineDolly = cineCam.GetComponent<CinemachineSplineDolly>();
        currentDistance = splineDolly.CameraPosition;

        yawExtension = cineCam.GetComponent<YawOverrideExtension>();
        if (yawExtension == null)
            yawExtension = cineCam.gameObject.AddComponent<YawOverrideExtension>();

        shakePerlin = cineCam.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>();

        initialScrollSpeed = scrollSpeedMetersPerPixel;

        if (speedZones == null || speedZones.Length == 0)
            speedZones = Object.FindObjectsByType<SpeedZone>(FindObjectsSortMode.None);

        if (animationZones == null || animationZones.Length == 0)
            animationZones = Object.FindObjectsByType<AnimationZone>(FindObjectsSortMode.None);

        fingersToScroll = Mathf.Clamp(fingersToScroll, 1, 5);
    }

    void OnDisable()
    {
        // Optional:
        // if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
    }

    void Update()
    {
        if (PauseManager.IsPaused) return; // Eingaben blockieren, wenn pausiert

        float maxDistance = splineDolly.Spline?.Spline?.GetLength() ?? 0f;
        float usableMaxDistance = Mathf.Max(0f, maxDistance - blockEndOffset);

        HandleInput();

        float effectiveVelocity = scrollVelocity;
        currentDistance = Mathf.Clamp(currentDistance + effectiveVelocity * Time.deltaTime, 0, usableMaxDistance);
        splineDolly.CameraPosition = currentDistance;

        // Clamp-Logik
        if ((currentDistance >= usableMaxDistance && scrollVelocity > 0f) ||
            (currentDistance <= 0f && scrollVelocity < 0f))
        {
            isClamped = true;
            scrollVelocity = 0f;
        }
        else
        {
            isClamped = false;
        }

        // Dämpfung
        float damping = 0.9f;
        float dampingFactor = Mathf.Pow(damping, Time.deltaTime * 60f);
        scrollVelocity *= dampingFactor;
        if (Mathf.Abs(scrollVelocity) < 0.001f)
            scrollVelocity = 0f;

        if (!isLooking)
            yawAmount = Mathf.Lerp(yawAmount, 0f, Time.deltaTime * 5f);

        if (yawExtension != null)
        {
            yawAmount = Mathf.Clamp(yawAmount, yawExtension.minYawLimit, yawExtension.maxYawLimit);
            yawExtension.yawOverride = yawAmount;
        }

        if (shakePerlin != null)
            shakePerlin.AmplitudeGain = (isScrolling || isLooking) ? 0f : 1f;

        HandleSpeedZones(maxDistance);
        HandleAnimationZones();
    }

    //---- SPEED ZONES -----
    private void HandleSpeedZones(float totalLength)
    {
        SpeedZone matchingZone = null;
        foreach (var zone in speedZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            float start = Mathf.Clamp(zone.startDistance, 0, totalLength);
            float end = Mathf.Clamp(zone.endDistance, start, totalLength);

            if (currentDistance >= start && currentDistance <= end)
            {
                matchingZone = zone;
                break;
            }
        }

        if (matchingZone != null && matchingZone != activeZone)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                matchingZone.newScrollSpeed, matchingZone.easeDuration, matchingZone.ease));
            activeZone = matchingZone;
        }
        else if (matchingZone == null && activeZone != null && activeZone.resetOnExit)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            float resetTo = activeZone.defaultSpeed > 0 ? activeZone.defaultSpeed : initialScrollSpeed;
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                resetTo, activeZone.resetEaseDuration, activeZone.resetEase));
            activeZone = null;
        }
    }

    // --- ANIMATIONZONEN (scrubben der Animationen nach Position) ---
    private void HandleAnimationZones()
    {
        if (animationZones == null) return;
        foreach (var zone in animationZones)
        {
            if (zone == null) continue;
            zone.ScrubAnimation(currentDistance);
        }
    }

    // NUR ScrollSpeed sanft ändern!
    private IEnumerator ChangeScrollSpeedSmoothly(float newSpeed, float duration, SpeedZone.EaseType ease)
    {
        float startSpeed = scrollSpeedMetersPerPixel;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float easedT = Ease(t, ease);
            scrollSpeedMetersPerPixel = Mathf.Lerp(startSpeed, newSpeed, easedT);
            yield return null;
        }
        scrollSpeedMetersPerPixel = newSpeed;
    }

    private float Ease(float t, SpeedZone.EaseType ease)
    {
        switch (ease)
        {
            case SpeedZone.EaseType.Linear: return t;
            case SpeedZone.EaseType.SmoothStep: return Mathf.SmoothStep(0f, 1f, t);
            case SpeedZone.EaseType.EaseIn: return t * t;
            case SpeedZone.EaseType.EaseOut: return t * (2 - t);
            case SpeedZone.EaseType.EaseInOut: return t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
            case SpeedZone.EaseType.SineWave: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            default: return t;
        }
    }

    // ---- USER INPUT (SCROLL & LOOK) ----
    void HandleInput()
    {
        // Mausstatus abfragen
        bool hasMouse = Mouse.current != null;
        bool leftPressed = hasMouse && Mouse.current.leftButton.isPressed;
        bool rightPressed = hasMouse && Mouse.current.rightButton.isPressed;
        bool mouseAny = leftPressed || rightPressed;

        // Touchstatus abfragen
        bool hasTouchDevice = Touchscreen.current != null;
        int touchCount = hasTouchDevice ? Touchscreen.current.touches.Count : 0;

        // --- SCROLL ---
        bool scrollGesture = false;
        Vector2 currentScrollPos = Vector2.zero;

        if (mouseAny) // Maus hat Vorrang vor Touch
        {
            if (leftPressed)
            {
                scrollGesture = true;
                currentScrollPos = Mouse.current.position.ReadValue();
            }
        }
        else if (hasTouchDevice && touchCount >= fingersToScroll) // >= ist robuster als ==
        {
            scrollGesture = true;
            currentScrollPos = Touchscreen.current.primaryTouch.position.ReadValue();
        }

        if (scrollGesture)
        {
            if (!isScrolling)
            {
                previousScrollPos = currentScrollPos;
                isScrolling = true;
            }
            else
            {
                float deltaY = currentScrollPos.y - previousScrollPos.y;
                ApplyScroll(deltaY);
                previousScrollPos = currentScrollPos;
            }
        }
        else
        {
            isScrolling = false;
        }

        // --- LOOK (Yaw beim Gedrückthalten & Draggen) ---
        bool lookGesture = false;
        Vector2 currentLookPos = Vector2.zero;

        if (mouseAny) // Maus hat Vorrang
        {
            if (rightPressed)
            {
                lookGesture = true;
                currentLookPos = Mouse.current.position.ReadValue();
            }
        }
        else if (hasTouchDevice && touchCount == 1 && fingersToScroll != 1)
        {
            lookGesture = true;
            currentLookPos = Touchscreen.current.primaryTouch.position.ReadValue();
        }

        if (lookGesture)
        {
            if (!isLooking)
            {
                previousLookPos = currentLookPos;
                isLooking = true;
            }
            else
            {
                Vector2 delta = currentLookPos - previousLookPos;
                ApplyLook(delta);
                previousLookPos = currentLookPos;
            }
        }
        else
        {
            isLooking = false;
        }
    }

    void ApplyScroll(float deltaY)
    {
        float direction = invertScroll ? -1f : 1f;
        scrollVelocity += deltaY * scrollSpeedMetersPerPixel * direction;
    }

    void ApplyLook(Vector2 delta)
    {
        float rawYawDelta = delta.x * lookSpeed;
        float nonLinearDamping = Mathf.Pow(Mathf.Abs(yawAmount), 1.2f);
        float dampFactor = 1f / (1f + nonLinearDamping * rotationDamping);
        float adjustedYawDelta = rawYawDelta * dampFactor;
        yawAmount += adjustedYawDelta;
    }

    void OnGUI()
    {
#if UNITY_EDITOR
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            normal = { textColor = Color.white }
        };

        float y = 10f;
        float line = 35f;

        GUI.Label(new Rect(10, y + 0 * line, 1000, line), $"Scroll Velocity: {scrollVelocity:F2}", style);
        GUI.Label(new Rect(10, y + 1 * line, 1000, line), $"Scroll Speed: {scrollSpeedMetersPerPixel:F4}", style);
        GUI.Label(new Rect(10, y + 2 * line, 1000, line), $"Actual Distance: {currentDistance:F2}", style);
        GUI.Label(new Rect(10, y + 3 * line, 1000, line), $"Yaw: {yawExtension?.yawOverride:F2}", style);
        GUI.Label(new Rect(10, y + 4 * line, 1000, line), $"Touches: {(Touchscreen.current?.touches.Count ?? 0)}", style);
        GUI.Label(new Rect(10, y + 5 * line, 1000, line), $"Clamped: {isClamped}", style);
        GUI.Label(new Rect(10, y + 6 * line, 1000, line), $"SpeedZone: {(activeZone ? activeZone.name : "keine")}", style);
#endif
    }
}





















using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class MovementController : MonoBehaviour
{
    [Header("Kamera & Bewegung")]
    public CinemachineCamera cineCam;
    public float scrollSpeedMetersPerPixel = 0.01f;

    [Header("Yaw & Look")]
    public float lookSpeed = 0.1f;
    public float rotationDamping = 0.1f;

    [Header("Zoom (echte Kamera)")]
    public float zoomedFOV = 30f;
    public float zoomDuration = 1.5f;
    public float zoomSpeed = 5f;

    [Header("Zoom-Einstellungen")]
    public float doubleClickThreshold = 0.3f;

    [Header("Eingabeoptionen")]
    public bool invertScroll = false;

    [Header("Spline-Ende blockieren")]
    public float blockEndOffset = 0.5f;

    [Header("GUI-Debug")]
    public bool showDebugGUI = true;

    [Header("SpeedZones (werden automatisch gefunden, falls leer)")]
    public SpeedZone[] speedZones;

    [Header("AnimationZones (werden automatisch gefunden, falls leer)")]
    public AnimationZone[] animationZones;

    [Header("TextZones (werden automatisch gefunden, falls leer)")]
    public TextZone[] textZones;

    private CinemachineSplineDolly splineDolly;
    private float scrollVelocity;
    private float currentDistance;

    private YawOverrideExtension yawExtension;
    private float yawAmount;

    private float defaultFOV;
    private bool isZooming;
    private Coroutine zoomCoroutine;
    private Coroutine scrollSpeedCoroutine;

    private Vector2 previousScrollPos;
    private Vector2 previousLookPos;
    private bool isScrolling;
    private bool isLooking;

    private CinemachineBasicMultiChannelPerlin shakePerlin;

    private float lastMouseClickTime = 0f;
    private Camera mainCam;

    private bool isClamped = false;
    private SpeedZone activeZone;
    private float initialScrollSpeed;

    void Start()
    {
        splineDolly = cineCam.GetComponent<CinemachineSplineDolly>();
        currentDistance = splineDolly.CameraPosition;

        yawExtension = cineCam.GetComponent<YawOverrideExtension>();
        if (yawExtension == null)
            yawExtension = cineCam.gameObject.AddComponent<YawOverrideExtension>();

        shakePerlin = cineCam.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>();

        mainCam = Camera.main;
        if (mainCam != null)
            defaultFOV = mainCam.fieldOfView;
        else
            Debug.LogWarning("Keine Kamera mit Tag MainCamera gefunden!");

        initialScrollSpeed = scrollSpeedMetersPerPixel;

        if (speedZones == null || speedZones.Length == 0)
            speedZones = Object.FindObjectsByType<SpeedZone>(FindObjectsSortMode.None);

        if (animationZones == null || animationZones.Length == 0)
            animationZones = Object.FindObjectsByType<AnimationZone>(FindObjectsSortMode.None);

        if (textZones == null || textZones.Length == 0)
            textZones = Object.FindObjectsByType<TextZone>(FindObjectsSortMode.None);
    }

    void Update()
    {
        float maxDistance = splineDolly.Spline?.Spline?.GetLength() ?? 0f;
        float usableMaxDistance = Mathf.Max(0f, maxDistance - blockEndOffset);

        // ----- TextZones zuerst prüfen! -----
        if (HandleTextZones()) return;

        HandleInput();

        float effectiveVelocity = scrollVelocity;
        currentDistance = Mathf.Clamp(currentDistance + effectiveVelocity * Time.deltaTime, 0, usableMaxDistance);
        splineDolly.CameraPosition = currentDistance;

        // Clamp-Logik wie gehabt
        if ((currentDistance >= usableMaxDistance && scrollVelocity > 0f) ||
            (currentDistance <= 0f && scrollVelocity < 0f))
        {
            isClamped = true;
            scrollVelocity = 0f;
        }
        else
        {
            isClamped = false;
        }

        float damping = 0.9f;
        float dampingFactor = Mathf.Pow(damping, Time.deltaTime * 60f);
        scrollVelocity *= dampingFactor;
        if (Mathf.Abs(scrollVelocity) < 0.001f)
            scrollVelocity = 0f;

        if (!isLooking)
            yawAmount = Mathf.Lerp(yawAmount, 0f, Time.deltaTime * 5f);

        if (yawExtension != null)
        {
            yawAmount = Mathf.Clamp(yawAmount, yawExtension.minYawLimit, yawExtension.maxYawLimit);
            yawExtension.yawOverride = yawAmount;
        }

        if (shakePerlin != null)
            shakePerlin.AmplitudeGain = (isScrolling || isLooking) ? 0f : 1f;

        HandleSpeedZones(maxDistance);
        HandleAnimationZones();
    }

    // ---- TEXT ZONES: Drag-Y-Scrolling für Seitenwechsel ----
    private bool HandleTextZones()
    {
        bool anyTextActive = false;
        foreach (var zone in textZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            bool isActive = zone.CheckAndTrigger(currentDistance, mainCam);

            // Drag-Y-Scroll-Input (wie Bewegung):
            if (isActive)
            {
                float deltaY = 0f;
                bool dragging = false;
                if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                {
                    Vector2 currentPos = Mouse.current.position.ReadValue();
                    if (!zone.isDragging)
                    {
                        zone.lastY = currentPos.y;
                        zone.isDragging = true;
                    }
                    else
                    {
                        deltaY = currentPos.y - zone.lastY;
                        zone.lastY = currentPos.y;
                        if (Mathf.Abs(deltaY) > 1f)
                            dragging = true;
                    }
                }
                else
                {
                    zone.isDragging = false;
                }
                if (dragging)
                    zone.HandleDragScroll(deltaY, mainCam);

                scrollVelocity = 0f; // Movement deaktiviert während Dialog!
            }
            anyTextActive |= isActive;
        }
        return anyTextActive;
    }

    //---- SPEED ZONES -----
    private void HandleSpeedZones(float totalLength)
    {
        SpeedZone matchingZone = null;
        foreach (var zone in speedZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            float start = Mathf.Clamp(zone.startDistance, 0, totalLength);
            float end = Mathf.Clamp(zone.endDistance, start, totalLength);

            if (currentDistance >= start && currentDistance <= end)
            {
                matchingZone = zone;
                break;
            }
        }

        if (matchingZone != null && matchingZone != activeZone)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                matchingZone.newScrollSpeed, matchingZone.easeDuration, matchingZone.ease));
            activeZone = matchingZone;
        }
        else if (matchingZone == null && activeZone != null && activeZone.resetOnExit)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            float resetTo = activeZone.defaultSpeed > 0 ? activeZone.defaultSpeed : initialScrollSpeed;
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                resetTo, activeZone.resetEaseDuration, activeZone.resetEase));
            activeZone = null;
        }
    }

    // --- ANIMATIONZONEN (scrubben der Animationen nach Position) ---
    private void HandleAnimationZones()
    {
        if (animationZones == null) return;
        foreach (var zone in animationZones)
        {
            if (zone == null) continue;
            zone.ScrubAnimation(currentDistance);
        }
    }

    // NUR ScrollSpeed sanft ändern!
    private IEnumerator ChangeScrollSpeedSmoothly(float newSpeed, float duration, SpeedZone.EaseType ease)
    {
        float startSpeed = scrollSpeedMetersPerPixel;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float easedT = Ease(t, ease);
            scrollSpeedMetersPerPixel = Mathf.Lerp(startSpeed, newSpeed, easedT);
            yield return null;
        }
        scrollSpeedMetersPerPixel = newSpeed;
    }

    private float Ease(float t, SpeedZone.EaseType ease)
    {
        switch (ease)
        {
            case SpeedZone.EaseType.Linear: return t;
            case SpeedZone.EaseType.SmoothStep: return Mathf.SmoothStep(0f, 1f, t);
            case SpeedZone.EaseType.EaseIn: return t * t;
            case SpeedZone.EaseType.EaseOut: return t * (2 - t);
            case SpeedZone.EaseType.EaseInOut: return t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
            case SpeedZone.EaseType.SineWave: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            default: return t;
        }
    }

    // ---- USER INPUT (SCROLL & LOOK, wie gehabt) ----
    void HandleInput()
    {
        bool leftPressed = Mouse.current?.leftButton.isPressed ?? false;
        bool rightPressed = Mouse.current?.rightButton.isPressed ?? false;
        int touchCount = Touchscreen.current?.touches.Count ?? 0;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (Time.time - lastMouseClickTime < doubleClickThreshold)
            {
                TriggerZoom();
            }
            lastMouseClickTime = Time.time;
        }

    #if UNITY_EDITOR
        if (leftPressed && !rightPressed)
    #else
        if (touchCount == 5)
    #endif
        {
            Vector2 currentPos = leftPressed
                ? Mouse.current.position.ReadValue()
                : Touchscreen.current.primaryTouch.position.ReadValue();

            if (!isScrolling)
            {
                previousScrollPos = currentPos;
                isScrolling = true;
            }
            else
            {
                float deltaY = currentPos.y - previousScrollPos.y;
                ApplyScroll(deltaY);
                previousScrollPos = currentPos;
            }
        }
        else isScrolling = false;

    #if UNITY_EDITOR
        if (rightPressed)
    #else
        if (touchCount == 1)
    #endif
        {
            Vector2 lookPos = rightPressed
                ? Mouse.current.position.ReadValue()
                : Touchscreen.current.primaryTouch.position.ReadValue();

            if (!isLooking)
            {
                previousLookPos = lookPos;
                isLooking = true;
            }
            else
            {
                Vector2 delta = lookPos - previousLookPos;
                ApplyLook(delta);
                previousLookPos = lookPos;
            }
        }
        else isLooking = false;
    }

    void ApplyScroll(float deltaY)
    {
        float direction = invertScroll ? -1f : 1f;
        scrollVelocity += deltaY * scrollSpeedMetersPerPixel * direction;
    }

    void ApplyLook(Vector2 delta)
    {
        float rawYawDelta = delta.x * lookSpeed;
        float nonLinearDamping = Mathf.Pow(Mathf.Abs(yawAmount), 1.2f);
        float dampFactor = 1f / (1f + nonLinearDamping * rotationDamping);
        float adjustedYawDelta = rawYawDelta * dampFactor;
        yawAmount += adjustedYawDelta;
    }

    void TriggerZoom()
    {
        if (isZooming || mainCam == null) return;

        if (zoomCoroutine != null)
            StopCoroutine(zoomCoroutine);

        zoomCoroutine = StartCoroutine(ZoomRoutine());
    }

    IEnumerator ZoomRoutine()
    {
        isZooming = true;

        while (Mathf.Abs(mainCam.fieldOfView - zoomedFOV) > 0.1f)
        {
            mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, zoomedFOV, Time.deltaTime * zoomSpeed);
            yield return null;
        }

        yield return new WaitForSeconds(zoomDuration);

        while (Mathf.Abs(mainCam.fieldOfView - defaultFOV) > 0.1f)
        {
            mainCam.fieldOfView = Mathf.Lerp(mainCam.fieldOfView, defaultFOV, Time.deltaTime * zoomSpeed);
            yield return null;
        }

        mainCam.fieldOfView = defaultFOV;
        isZooming = false;
    }

    void OnGUI()
    {
    #if UNITY_EDITOR
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            normal = { textColor = Color.white }
        };

        float y = 10f;
        float line = 35f;

        GUI.Label(new Rect(10, y + 0 * line, 1000, line), $"Scroll Velocity: {scrollVelocity:F2}", style);
        GUI.Label(new Rect(10, y + 1 * line, 1000, line), $"Scroll Speed: {scrollSpeedMetersPerPixel:F4}", style);
        GUI.Label(new Rect(10, y + 2 * line, 1000, line), $"Actual Distance: {currentDistance:F2}", style);
        GUI.Label(new Rect(10, y + 3 * line, 1000, line), $"Yaw: {yawExtension?.yawOverride:F2}", style);
        GUI.Label(new Rect(10, y + 4 * line, 1000, line), $"Zooming: {isZooming}", style);
        GUI.Label(new Rect(10, y + 5 * line, 1000, line), $"Touches: {(Touchscreen.current?.touches.Count ?? 0)}", style);
        GUI.Label(new Rect(10, y + 6 * line, 1000, line), $"Clamped: {isClamped}", style);
        GUI.Label(new Rect(10, y + 7 * line, 1000, line), $"SpeedZone: {(activeZone ? activeZone.name : "keine")}", style);
    #endif
    }
}













using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class MovementController : MonoBehaviour
{
    [Header("Kamera & Bewegung")]
    public CinemachineCamera cineCam;
    public float scrollSpeedMetersPerPixel = 0.01f;

    [Header("Yaw & Look")]
    public float lookSpeed = 0.1f;
    public float rotationDamping = 0.1f;

    [Header("Eingabeoptionen")]
    public bool invertScroll = false;

    [Header("Spline-Ende blockieren")]
    public float blockEndOffset = 0.5f;

    [Header("GUI-Debug")]
    public bool showDebugGUI = true;

    [Header("SpeedZones (werden automatisch gefunden, falls leer)")]
    public SpeedZone[] speedZones;

    [Header("AnimationZones (werden automatisch gefunden, falls leer)")]
    public AnimationZone[] animationZones;

    [Header("Touch-Einstellungen")]
    [Range(1,5)]
    public int fingersToScroll = 2;

    private CinemachineSplineDolly splineDolly;
    private float scrollVelocity;
    private float currentDistance;

    private YawOverrideExtension yawExtension;
    private float yawAmount;

    private Coroutine scrollSpeedCoroutine;

    private Vector2 previousScrollPos;
    private Vector2 previousLookPos;
    private bool isScrolling;
    private bool isLooking;

    private CinemachineBasicMultiChannelPerlin shakePerlin;

    private bool isClamped = false;
    private SpeedZone activeZone;
    private float initialScrollSpeed;

    void Start()
    {
        splineDolly = cineCam.GetComponent<CinemachineSplineDolly>();
        currentDistance = splineDolly.CameraPosition;

        yawExtension = cineCam.GetComponent<YawOverrideExtension>();
        if (yawExtension == null)
            yawExtension = cineCam.gameObject.AddComponent<YawOverrideExtension>();

        shakePerlin = cineCam.GetComponentInChildren<CinemachineBasicMultiChannelPerlin>();

        initialScrollSpeed = scrollSpeedMetersPerPixel;

        if (speedZones == null || speedZones.Length == 0)
            speedZones = Object.FindObjectsByType<SpeedZone>(FindObjectsSortMode.None);

        if (animationZones == null || animationZones.Length == 0)
            animationZones = Object.FindObjectsByType<AnimationZone>(FindObjectsSortMode.None);

        fingersToScroll = Mathf.Clamp(fingersToScroll, 1, 5);
    }

    void Update()
    {
        float maxDistance = splineDolly.Spline?.Spline?.GetLength() ?? 0f;
        float usableMaxDistance = Mathf.Max(0f, maxDistance - blockEndOffset);

        HandleInput();

        float effectiveVelocity = scrollVelocity;
        currentDistance = Mathf.Clamp(currentDistance + effectiveVelocity * Time.deltaTime, 0, usableMaxDistance);
        splineDolly.CameraPosition = currentDistance;

        // Clamp-Logik
        if ((currentDistance >= usableMaxDistance && scrollVelocity > 0f) ||
            (currentDistance <= 0f && scrollVelocity < 0f))
        {
            isClamped = true;
            scrollVelocity = 0f;
        }
        else
        {
            isClamped = false;
        }

        // Dämpfung
        float damping = 0.9f;
        float dampingFactor = Mathf.Pow(damping, Time.deltaTime * 60f);
        scrollVelocity *= dampingFactor;
        if (Mathf.Abs(scrollVelocity) < 0.001f)
            scrollVelocity = 0f;

        if (!isLooking)
            yawAmount = Mathf.Lerp(yawAmount, 0f, Time.deltaTime * 5f);

        if (yawExtension != null)
        {
            yawAmount = Mathf.Clamp(yawAmount, yawExtension.minYawLimit, yawExtension.maxYawLimit);
            yawExtension.yawOverride = yawAmount;
        }

        if (shakePerlin != null)
            shakePerlin.AmplitudeGain = (isScrolling || isLooking) ? 0f : 1f;

        HandleSpeedZones(maxDistance);
        HandleAnimationZones();
    }

    //---- SPEED ZONES -----
    private void HandleSpeedZones(float totalLength)
    {
        SpeedZone matchingZone = null;
        foreach (var zone in speedZones)
        {
            if (zone == null || zone.spline != splineDolly.Spline) continue;
            float start = Mathf.Clamp(zone.startDistance, 0, totalLength);
            float end = Mathf.Clamp(zone.endDistance, start, totalLength);

            if (currentDistance >= start && currentDistance <= end)
            {
                matchingZone = zone;
                break;
            }
        }

        if (matchingZone != null && matchingZone != activeZone)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                matchingZone.newScrollSpeed, matchingZone.easeDuration, matchingZone.ease));
            activeZone = matchingZone;
        }
        else if (matchingZone == null && activeZone != null && activeZone.resetOnExit)
        {
            if (scrollSpeedCoroutine != null) StopCoroutine(scrollSpeedCoroutine);
            float resetTo = activeZone.defaultSpeed > 0 ? activeZone.defaultSpeed : initialScrollSpeed;
            scrollSpeedCoroutine = StartCoroutine(ChangeScrollSpeedSmoothly(
                resetTo, activeZone.resetEaseDuration, activeZone.resetEase));
            activeZone = null;
        }
    }

    // --- ANIMATIONZONEN (scrubben der Animationen nach Position) ---
    private void HandleAnimationZones()
    {
        if (animationZones == null) return;
        foreach (var zone in animationZones)
        {
            if (zone == null) continue;
            zone.ScrubAnimation(currentDistance);
        }
    }

    // NUR ScrollSpeed sanft ändern!
    private IEnumerator ChangeScrollSpeedSmoothly(float newSpeed, float duration, SpeedZone.EaseType ease)
    {
        float startSpeed = scrollSpeedMetersPerPixel;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float easedT = Ease(t, ease);
            scrollSpeedMetersPerPixel = Mathf.Lerp(startSpeed, newSpeed, easedT);
            yield return null;
        }
        scrollSpeedMetersPerPixel = newSpeed;
    }

    private float Ease(float t, SpeedZone.EaseType ease)
    {
        switch (ease)
        {
            case SpeedZone.EaseType.Linear: return t;
            case SpeedZone.EaseType.SmoothStep: return Mathf.SmoothStep(0f, 1f, t);
            case SpeedZone.EaseType.EaseIn: return t * t;
            case SpeedZone.EaseType.EaseOut: return t * (2 - t);
            case SpeedZone.EaseType.EaseInOut: return t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
            case SpeedZone.EaseType.SineWave: return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            default: return t;
        }
    }

    // ---- USER INPUT (SCROLL & LOOK) ----
    void HandleInput()
    {
        bool leftPressed = Mouse.current?.leftButton.isPressed ?? false;
        bool rightPressed = Mouse.current?.rightButton.isPressed ?? false;
        int touchCount = Touchscreen.current?.touches.Count ?? 0;

        // --- SCROLL ---
#if UNITY_EDITOR
        if (leftPressed && !rightPressed)
#else
        if (touchCount == fingersToScroll)
#endif
        {
            Vector2 currentPos =
#if UNITY_EDITOR
                Mouse.current.position.ReadValue();
#else
                Touchscreen.current.primaryTouch.position.ReadValue();
#endif
            if (!isScrolling)
            {
                previousScrollPos = currentPos;
                isScrolling = true;
            }
            else
            {
                float deltaY = currentPos.y - previousScrollPos.y;
                ApplyScroll(deltaY);
                previousScrollPos = currentPos;
            }
        }
        else isScrolling = false;

        // --- LOOK (Yaw beim Gedrückthalten & Draggen) ---
#if UNITY_EDITOR
        if (rightPressed)
#else
        if (touchCount == 1 && fingersToScroll != 1)
#endif
        {
            Vector2 lookPos =
#if UNITY_EDITOR
                Mouse.current.position.ReadValue();
#else
                Touchscreen.current.primaryTouch.position.ReadValue();
#endif
            if (!isLooking)
            {
                previousLookPos = lookPos;
                isLooking = true;
            }
            else
            {
                Vector2 delta = lookPos - previousLookPos;
                ApplyLook(delta);
                previousLookPos = lookPos;
            }
        }
        else isLooking = false;
    }

    void ApplyScroll(float deltaY)
    {
        float direction = invertScroll ? -1f : 1f;
        scrollVelocity += deltaY * scrollSpeedMetersPerPixel * direction;
    }

    void ApplyLook(Vector2 delta)
    {
        float rawYawDelta = delta.x * lookSpeed;
        float nonLinearDamping = Mathf.Pow(Mathf.Abs(yawAmount), 1.2f);
        float dampFactor = 1f / (1f + nonLinearDamping * rotationDamping);
        float adjustedYawDelta = rawYawDelta * dampFactor;
        yawAmount += adjustedYawDelta;
    }

    void OnGUI()
    {
#if UNITY_EDITOR
        if (!showDebugGUI) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 30,
            normal = { textColor = Color.white }
        };

        float y = 10f;
        float line = 35f;

        GUI.Label(new Rect(10, y + 0 * line, 1000, line), $"Scroll Velocity: {scrollVelocity:F2}", style);
        GUI.Label(new Rect(10, y + 1 * line, 1000, line), $"Scroll Speed: {scrollSpeedMetersPerPixel:F4}", style);
        GUI.Label(new Rect(10, y + 2 * line, 1000, line), $"Actual Distance: {currentDistance:F2}", style);
        GUI.Label(new Rect(10, y + 3 * line, 1000, line), $"Yaw: {yawExtension?.yawOverride:F2}", style);
        GUI.Label(new Rect(10, y + 4 * line, 1000, line), $"Touches: {(Touchscreen.current?.touches.Count ?? 0)}", style);
        GUI.Label(new Rect(10, y + 5 * line, 1000, line), $"Clamped: {isClamped}", style);
        GUI.Label(new Rect(10, y + 6 * line, 1000, line), $"SpeedZone: {(activeZone ? activeZone.name : "keine")}", style);
#endif
    }
}


*/