using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class RotationController : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 0.2f;     // ° pro Pixel-Skala
    [SerializeField, Range(0f, 1f)] private float damping = 0.9f; // Trägheitsdämpfung pro Frame
    [SerializeField] private float minVelocity = 0.05f;       // Trägheits-Grenze (stoppt darunter)
    [SerializeField] private bool invertDirection = true;

    [Header("Targets")]
    [SerializeField] private Transform rotationTarget;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private float lookAtSpeed = 2f;

    [Header("Eingabe (Hold-to-Rotate)")]
    [Tooltip("Bewegungen unterhalb dieses Pixelwerts werden ignoriert.")]
    [SerializeField] private float deadzonePixels = 2f;
    [Tooltip("Pro Frame maximal erlaubte Pixelbewegung (gegen Ausreißer).")]
    [SerializeField] private float maxDeltaPixelsPerFrame = 250f;
    [Tooltip("Nutze Pointer-Delta des Input Systems statt Positionsdifferenz (vermeidet Sprung im Press-Frame).")]
    [SerializeField] private bool usePointerDelta = true;
    [Tooltip("Glättung der gemessenen Drag-Delta (höher = glatter).")]
    [SerializeField, Range(0f, 1f)] private float deltaSmoothing = 0.25f;

    [Header("Inertia / Tap-Filter")]
    [Tooltip("Haltezeit darunter zählt als Tap – keine Trägheit.")]
    [SerializeField] private float tapMaxSeconds = 0.15f;
    [Tooltip("Gesamtweg (Pixel) ab dem Momentum 'scharf' ist.")]
    [SerializeField] private float noInertiaPixels = 10f;
    [Tooltip("Min. Winkelgeschwindigkeit (deg/s) beim Loslassen, sonst keine Trägheit.")]
    [SerializeField] private float releaseVelocityMinDegPerSec = 45f;

    // Debug
    private string debugHit = "Kein Hit";
    private bool debugHolding;
    private Vector3 debugVelocity;
    private string debugLookAtStatus = "-";
    private bool debugMomentumArmed;

    // State
    private Camera mainCamera;
    private Mouse mouse;
    private Touchscreen touchscreen;

    private bool isHolding = false;        // „Taste gedrückt & über gültigem Ziel gestartet“
    private bool skipNextDelta = false;    // ignoriert den ersten Frame nach Press
    private Vector2 lastPos;               // für Pos-basiertes Delta
    private Vector2 smoothedDelta;         // geglättetes Drag-Delta (Bildschirmebene)
    private float holdStartTime;
    private float accumulatedPathPixels;   // Summe der absoluten Pixelbewegung seit Hold-Beginn
    private bool momentumArmed;            // wird erst true, wenn accumulatedPathPixels >= noInertiaPixels

    private Vector3 currentVelocity;       // Trägheits-„Angular“-Vektor im Welt-Raum (deg/sec skaliert)
    private float lastAngularSpeedDegPerSec; // für Release-Gate
    private Coroutine lookAtCoroutine;

    void Start()
    {
        mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        if (!rotationTarget) rotationTarget = transform;

        mouse = Mouse.current;
        touchscreen = Touchscreen.current;
    }

    void Update()
    {
        HandleInput();

        // Inertia (nur wenn nicht gehalten & kein LookAt läuft)
        if (!isHolding && lookAtCoroutine == null)
        {
            if (currentVelocity.magnitude > minVelocity)
            {
                // Keine extra Invertierung mehr: currentVelocity enthält bereits Richtung.
                var step = currentVelocity * Time.deltaTime;
                rotationTarget.Rotate(step, Space.World);

                currentVelocity *= damping;
            }
            else
            {
                currentVelocity = Vector3.zero;
            }
        }

        debugHolding = isHolding;
        debugVelocity = currentVelocity;
        debugMomentumArmed = momentumArmed;
    }

    void HandleInput()
    {
        bool mousePressedThisFrame = mouse != null && mouse.leftButton.wasPressedThisFrame;
        bool touchPressedThisFrame = touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame;

        // ---- PRESS: Start nur, wenn auf diesem Objekt (oder Child) getroffen wird
        if (mousePressedThisFrame || touchPressedThisFrame)
        {
            Vector2 pressPos = mousePressedThisFrame
                ? mouse.position.ReadValue()
                : touchscreen.primaryTouch.position.ReadValue();

            Ray ray = mainCamera.ScreenPointToRay(pressPos);
            if (Physics.Raycast(ray, out var hit))
            {
                debugHit = hit.collider.name;

                // Tap auf Fokus-Target: sanftes Ausrichten
                if (focusTarget != null && hit.transform == focusTarget)
                {
                    StartLookAtFocusTarget();
                    return;
                }

                // Start Hold-to-Rotate nur, wenn auf dem eigenen Objekt (oder Kind)
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    BeginHold(pressPos);
                    StopLookAt();
                }
            }
            else
            {
                debugHit = "Nichts getroffen";
            }
        }

        // ---- HOLD/DRAG: nur während Taste/Touch gehalten wird
        bool mouseIsDown = mouse != null && mouse.leftButton.isPressed;
        bool touchIsDown = touchscreen != null && touchscreen.primaryTouch.press.isPressed;
        bool anyDown = mouseIsDown || touchIsDown;

        if (isHolding && anyDown)
        {
            // Nicht im gleichen Frame wie Press rotieren
            bool justPressed = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                               (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame);

            if (!justPressed)
            {
                if (usePointerDelta)
                {
                    Vector2 delta = GetPointerDeltaClamped();
                    // Path-Länge akkumulieren (|dx|+|dy|, robust für verschiedene Richtungen)
                    accumulatedPathPixels += Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                    if (!momentumArmed && accumulatedPathPixels >= noInertiaPixels) momentumArmed = true;

                    ApplyDragDelta(delta); // rotiert + baut Velocity auf
                }
                else
                {
                    Vector2 pos = GetPointerPosition();
                    Vector2 delta = pos - lastPos;
                    lastPos = pos;

                    delta = ClampDelta(delta);
                    accumulatedPathPixels += Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
                    if (!momentumArmed && accumulatedPathPixels >= noInertiaPixels) momentumArmed = true;

                    ApplyDragDelta(delta);
                }
            }
        }

        // ---- RELEASE
        bool mouseReleased = mouse != null && mouse.leftButton.wasReleasedThisFrame;
        bool touchReleased = touchscreen != null && touchscreen.primaryTouch.press.wasReleasedThisFrame;
        if (mouseReleased || touchReleased)
        {
            EndHold();
        }
    }

    // -------- Hold Lifecycle --------

    void BeginHold(Vector2 pressPos)
    {
        isHolding = true;
        skipNextDelta = true;      // verhindert Sprung im ersten Drag-Frame
        lastPos = pressPos;
        smoothedDelta = Vector2.zero;
        accumulatedPathPixels = 0f;
        momentumArmed = false;
        holdStartTime = Time.time;
        lastAngularSpeedDegPerSec = 0f;
        // currentVelocity bleibt unangetastet, Inertia-Update pausiert bei isHolding==true
    }

    void EndHold()
    {
        isHolding = false;
        skipNextDelta = false;

        // TAP-FILTERS:
        bool wasTapDuration = (Time.time - holdStartTime) <= tapMaxSeconds;
        bool wasTinyMove = accumulatedPathPixels < noInertiaPixels;
        bool lowReleaseSpeed = lastAngularSpeedDegPerSec < releaseVelocityMinDegPerSec;

        // Nur wenn ALLE Filter sagen „ok“ lassen wir Momentum laufen
        if (wasTapDuration || wasTinyMove || !momentumArmed || lowReleaseSpeed)
        {
            currentVelocity = Vector3.zero;
        }

        // Puffer zurücksetzen
        smoothedDelta = Vector2.zero;
        accumulatedPathPixels = 0f;
        momentumArmed = false;
        lastAngularSpeedDegPerSec = 0f;
    }

    // -------- Drag / Rotation --------

    void ApplyDragDelta(Vector2 rawDelta)
    {
        if (skipNextDelta)
        {
            if (!usePointerDelta) lastPos = GetPointerPosition();
            skipNextDelta = false;
            return;
        }

        // Deadzone
        if (rawDelta.sqrMagnitude < deadzonePixels * deadzonePixels)
            return;

        // Smoothing (einfacher Low-Pass in Bildschirmebene)
        smoothedDelta = Vector2.Lerp(smoothedDelta, rawDelta, 1f - Mathf.Clamp01(1f - deltaSmoothing));

        // Richtung 1x berücksichtigen – NICHT später nochmal!
        float direction = invertDirection ? -1f : 1f;

        // Bildschirm-Delta -> Achse: xDrag dreht um Welt-Y, yDrag um Welt-X (über Kamera orientiert)
        Vector3 dragAxisScreen = new Vector3(-smoothedDelta.y, smoothedDelta.x, 0f);
        if (dragAxisScreen == Vector3.zero) return;

        float dragMagnitude = smoothedDelta.magnitude * rotationSpeed; // „Grad“ Skala
        Vector3 worldAxis = mainCamera.transform.TransformDirection(dragAxisScreen.normalized);

        // Sofortige Rotation
        Quaternion rot = Quaternion.AngleAxis(dragMagnitude * direction, worldAxis);
        rotationTarget.rotation = rot * rotationTarget.rotation;

        // Trägheitsgeschwindigkeit (deg/sec): aus „Grad pro Frame“ -> pro Sekunde
        Vector3 perFrameAngular = worldAxis * (dragMagnitude * direction);
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        currentVelocity = perFrameAngular / dt;

        // Für Release-Gate merken
        lastAngularSpeedDegPerSec = perFrameAngular.magnitude / dt;
    }

    Vector2 GetPointerPosition()
    {
        if (mouse != null && mouse.leftButton.isPressed) return mouse.position.ReadValue();
        if (touchscreen != null && touchscreen.primaryTouch.press.isPressed) return touchscreen.primaryTouch.position.ReadValue();
        return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
    }

    Vector2 GetPointerDeltaClamped()
    {
        Vector2 d = Vector2.zero;
        if (mouse != null && mouse.leftButton.isPressed) d = mouse.delta.ReadValue();
        else if (touchscreen != null && touchscreen.primaryTouch.press.isPressed) d = touchscreen.primaryTouch.delta.ReadValue();
        return ClampDelta(d);
    }

    Vector2 ClampDelta(Vector2 d)
    {
        float mag = d.magnitude;
        if (mag > maxDeltaPixelsPerFrame) d = d * (maxDeltaPixelsPerFrame / mag);
        return d;
    }

    // -------- LookAt (wie gehabt) --------

    void StartLookAtFocusTarget()
    {
        if (lookAtCoroutine != null) StopCoroutine(lookAtCoroutine);
        lookAtCoroutine = StartCoroutine(SmoothLookAtTarget());
    }

    void StopLookAt()
    {
        if (lookAtCoroutine != null)
        {
            StopCoroutine(lookAtCoroutine);
            lookAtCoroutine = null;
        }
    }

    IEnumerator SmoothLookAtTarget()
    {
        isHolding = false;
        currentVelocity = Vector3.zero;

        Vector3 startDir = focusTarget.position - rotationTarget.position;
        Vector3 targetDir = mainCamera.transform.position - rotationTarget.position;

        Quaternion initialRot = rotationTarget.rotation;
        Quaternion targetRot = Quaternion.FromToRotation(startDir, targetDir) * rotationTarget.rotation;

        float t = 0f;
        debugLookAtStatus = "Drehe mit EaseOut...";

        while (t < 1f)
        {
            if (isHolding)
            {
                debugLookAtStatus = "Abgebrochen durch Hold";
                yield break;
            }

            t += Time.deltaTime * lookAtSpeed;
            float easedT = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f); // Cubic Ease-Out

            rotationTarget.rotation = Quaternion.Slerp(initialRot, targetRot, easedT);
            yield return null;
        }

        rotationTarget.rotation = targetRot;
        lookAtCoroutine = null;
        debugLookAtStatus = "Abgeschlossen";
    }

    void OnValidate()
    {
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        minVelocity = Mathf.Max(0f, minVelocity);
        lookAtSpeed = Mathf.Max(0.01f, lookAtSpeed);
        deadzonePixels = Mathf.Max(0f, deadzonePixels);
        maxDeltaPixelsPerFrame = Mathf.Max(1f, maxDeltaPixelsPerFrame);
        tapMaxSeconds = Mathf.Max(0f, tapMaxSeconds);
        noInertiaPixels = Mathf.Max(0f, noInertiaPixels);
        releaseVelocityMinDegPerSec = Mathf.Max(0f, releaseVelocityMinDegPerSec);
    }

#if UNITY_EDITOR
    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
        float line = 20f; float y = 10f;
        GUI.Label(new Rect(10, y += line, 600, line), $"Holding: {debugHolding}", style);
        GUI.Label(new Rect(10, y += line, 600, line), $"Hit: {debugHit}", style);
        GUI.Label(new Rect(10, y += line, 600, line), $"Velocity: {debugVelocity}", style);
        GUI.Label(new Rect(10, y += line, 600, line), $"Momentum Armed: {debugMomentumArmed}", style);
        GUI.Label(new Rect(10, y += line, 600, line), $"LookAt: {debugLookAtStatus}", style);
    }
#endif
}


/*
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class RotationController : MonoBehaviour
{
    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 0.2f;
    [SerializeField, Range(0f, 1f)] private float damping = 0.9f;
    [SerializeField] private float minVelocity = 0.05f;
    [SerializeField] private bool invertDirection = true;

    [Header("Target Settings")]
    [SerializeField] private Transform rotationTarget;
    [SerializeField] private Transform focusTarget;
    [SerializeField] private float lookAtSpeed = 2f;

    // Debug Info
    private string debugHit = "Kein Hit";
    private Vector2 debugInputPosition;
    private bool debugDragging;
    private Vector3 debugVelocity;
    private string debugLookAtStatus = "-";

    private bool isDragging = false;
    private Vector2 lastInputPosition;
    private Vector3 currentVelocity;
    private Camera mainCamera;

    private Coroutine lookAtCoroutine;

    private Mouse mouse;
    private Touchscreen touchscreen;

    void Start()
    {
        mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        if (rotationTarget == null)
            rotationTarget = transform;

        mouse = Mouse.current;
        touchscreen = Touchscreen.current;
    }

    void Update()
    {
        HandleInput();

        if (!isDragging && lookAtCoroutine == null)
        {
            if (currentVelocity.magnitude > minVelocity)
            {
                Vector3 rotationStep = currentVelocity * (invertDirection ? -1f : 1f) * Time.deltaTime;
                rotationTarget.Rotate(rotationStep, Space.World);
                currentVelocity *= damping;
            }
            else
            {
                currentVelocity = Vector3.zero;
            }
        }

        debugDragging = isDragging;
        debugVelocity = currentVelocity;
    }

    void HandleInput()
    {
        Vector2? inputPosition = null;

        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            inputPosition = mouse.position.ReadValue();
        else if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            inputPosition = touchscreen.primaryTouch.position.ReadValue();

        if (inputPosition.HasValue)
        {
            debugInputPosition = inputPosition.Value;

            Ray ray = mainCamera.ScreenPointToRay(inputPosition.Value);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                debugHit = hit.collider.name;

                if (focusTarget != null && hit.transform == focusTarget)
                {
                    StartLookAtFocusTarget();
                    return;
                }

                if (!isDragging && (hit.transform == transform || hit.transform.IsChildOf(transform)))
                {
                    StartDragging(inputPosition.Value);
                    StopLookAt();
                }
            }
            else
            {
                debugHit = "Nichts getroffen";
            }
        }

        if (isDragging)
        {
            Vector2 currentPos = mouse != null ? mouse.position.ReadValue() : touchscreen.primaryTouch.position.ReadValue();
            UpdateDragging(currentPos);
        }

        if ((mouse != null && mouse.leftButton.wasReleasedThisFrame) ||
            (touchscreen != null && touchscreen.primaryTouch.press.wasReleasedThisFrame))
        {
            StopDragging();
        }
    }

    void StartDragging(Vector2 inputPosition)
    {
        isDragging = true;
        lastInputPosition = inputPosition;
        currentVelocity = Vector3.zero;
    }

    void UpdateDragging(Vector2 inputPosition)
    {
        Vector2 delta = inputPosition - lastInputPosition;
        lastInputPosition = inputPosition;

        if (delta == Vector2.zero)
            return;

        float direction = invertDirection ? -1f : 1f;

        Vector3 dragAxis = new Vector3(-delta.y, delta.x, 0f).normalized;
        float dragMagnitude = delta.magnitude * rotationSpeed;

        Quaternion rotation = Quaternion.AngleAxis(dragMagnitude * direction, mainCamera.transform.TransformDirection(dragAxis));
        rotationTarget.rotation = rotation * rotationTarget.rotation;

        currentVelocity = mainCamera.transform.TransformDirection(dragAxis * dragMagnitude / Time.deltaTime);
    }

    void StopDragging()
    {
        isDragging = false;
    }

    void StartLookAtFocusTarget()
    {
        if (lookAtCoroutine != null)
            StopCoroutine(lookAtCoroutine);

        lookAtCoroutine = StartCoroutine(SmoothLookAtTarget());
    }

    void StopLookAt()
    {
        if (lookAtCoroutine != null)
        {
            StopCoroutine(lookAtCoroutine);
            lookAtCoroutine = null;
        }
    }

    IEnumerator SmoothLookAtTarget()
    {
        isDragging = false;
        currentVelocity = Vector3.zero;

        Vector3 startDirection = focusTarget.position - rotationTarget.position;
        Vector3 targetDirection = mainCamera.transform.position - rotationTarget.position;

        Quaternion initialRotation = rotationTarget.rotation;
        Quaternion targetRotation = Quaternion.FromToRotation(startDirection, targetDirection) * rotationTarget.rotation;

        float t = 0f;
        debugLookAtStatus = "Drehe mit EaseOut...";

        while (t < 1f)
        {
            if (isDragging)
            {
                debugLookAtStatus = "Abgebrochen durch Drag";
                yield break;
            }

            t += Time.deltaTime * lookAtSpeed;

            // Cubic Ease-Out
            float easedT = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

            rotationTarget.rotation = Quaternion.Slerp(initialRotation, targetRotation, easedT);
            yield return null;
        }

        rotationTarget.rotation = targetRotation;
        lookAtCoroutine = null;
        debugLookAtStatus = "Abgeschlossen";
    }

    void OnValidate()
    {
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        minVelocity = Mathf.Max(0f, minVelocity);
        lookAtSpeed = Mathf.Max(0.01f, lookAtSpeed);
    }

    
}

*/