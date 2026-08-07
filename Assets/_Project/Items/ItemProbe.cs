using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(ItemIdentity))]
public class ItemProbe : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float shakeThreshold = 0.05f;
    [SerializeField] private float shakeCooldown = 0.5f;
    [SerializeField] private float hapticMassMultiplier = 0.1f;
    [SerializeField] private float glowDistance = 0.5f;

    private XRGrabInteractable grabInteractable;
    private ItemIdentity itemIdentity;
    private AudioSource audioSource;
    private Renderer itemRenderer;
    private MaterialPropertyBlock propBlock;

    private Vector3 lastPosition;
    private bool wasSelected;
    private float nextShakeTime;
    private int shakeCount;

    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    public int ShakeCount
    {
        get => shakeCount;
        set => shakeCount = value;
    }

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        itemIdentity = GetComponent<ItemIdentity>();
        audioSource = GetComponent<AudioSource>();
        itemRenderer = GetComponent<Renderer>();
        if (itemRenderer == null)
        {
            itemRenderer = GetComponentInChildren<Renderer>();
        }
        propBlock = new MaterialPropertyBlock();

        // URP/Lit'te sadece PropertyBlock yazmak yetmez: emission keyword'u kapaliysa
        // renk hesaplanmaz. Her prefabdaki paylasilan materyalde bunu garanti ederiz.
        if (itemRenderer != null && itemRenderer.sharedMaterial != null)
        {
            itemRenderer.sharedMaterial.EnableKeyword("_EMISSION");
        }
    }

    private void Update()
    {
        if (itemIdentity == null || itemIdentity.ItemData == null)
            return;

        ItemData data = itemIdentity.ItemData;

        // "Ele alinmayan obje sallanmaz."
        // grabInteractable.isSelected, esyayi DOLABIN SOKETI tuttugunda da true doner.
        // Uc yoklama kanali da yalnizca esya bir ELDE iken calismali; bu yuzden
        // soket disindaki bir interactor ariyoruz.
        bool isSelected = IsHeldByHand();

        // --- 1. Shaking Channel (Sesli) ---
        if (data.category == ItemCategory.Sesli)
        {
            if (isSelected)
            {
                if (wasSelected)
                {
                    float frameMovement = Vector3.Distance(transform.position, lastPosition);
                    if (frameMovement > shakeThreshold && Time.time >= nextShakeTime)
                    {
                        PlayRattle(data.rattleClip);
                        shakeCount++;
                        nextShakeTime = Time.time + shakeCooldown;
                    }
                }
                lastPosition = transform.position;
                wasSelected = true;
            }
            else
            {
                wasSelected = false;
            }
        }

        // --- 2. Glowing Channel (Parlak) ---
        if (data.category == ItemCategory.Parlak)
        {
            bool shouldGlow = false;
            // Referanstaki kural "eldeyken" degil, esya kameraya yakindayken
            // parlamasidir. Bu nedenle oyuncu masadaki esyaya yaklasinca da sinyal
            // gorunur; kamera/elde tutma bagimliligi parlamayi sessizce kapatmaz.
            Camera mainCam = PlayerRefs.Instance != null ? PlayerRefs.Instance.MainCamera : Camera.main;
            if (mainCam != null)
            {
                float dist = Vector3.Distance(transform.position, mainCam.transform.position);
                shouldGlow = dist < glowDistance;
            }

            Color targetColor = shouldGlow ? data.glowColor : Color.black;
            SetEmissionColor(targetColor);
        }
        else
        {
            // Ensure non-glowing items have emission color set to black
            SetEmissionColor(Color.black);
        }

        // --- 3. Weighting Channel (Agir) ---
        if (data.category == ItemCategory.Agir && isSelected)
        {
            float amplitude = Mathf.Clamp01(data.mass * hapticMassMultiplier);
            // Send continuous haptics every frame. Using Time.deltaTime ensures the controller vibrates continuously while held.
            foreach (var interactor in grabInteractable.interactorsSelecting)
            {
                if (interactor is XRBaseInputInteractor controllerInteractor)
                {
                    controllerInteractor.SendHapticImpulse(amplitude, Time.deltaTime);
                }
            }
        }
    }

    /// <summary>
    /// Esya bir EL tarafindan mi tutuluyor? Soket (XRSocketInteractor) tutuyorsa false doner.
    /// </summary>
    private bool IsHeldByHand()
    {
        if (grabInteractable == null || !grabInteractable.isSelected)
            return false;

        foreach (IXRSelectInteractor interactor in grabInteractable.interactorsSelecting)
        {
            if (interactor is XRSocketInteractor)
                continue;

            return true;
        }

        return false;
    }

    private void PlayRattle(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void SetEmissionColor(Color color)
    {
        if (itemRenderer != null)
        {
            itemRenderer.GetPropertyBlock(propBlock);
            propBlock.SetColor(EmissionColorProp, color);
            itemRenderer.SetPropertyBlock(propBlock);
        }
    }
}
