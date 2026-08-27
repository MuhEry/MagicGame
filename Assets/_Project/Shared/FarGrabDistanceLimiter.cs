using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public sealed class FarGrabDistanceLimiter : MonoBehaviour
{
    [Min(0.11f)]
    [SerializeField] private float minimumDistance = 0.2f;

    private readonly List<Binding> bindings = new();

    private void OnEnable()
    {
        BindInteractors();
    }

    private void Start()
    {
        if (bindings.Count == 0)
            BindInteractors();

        Debug.Log($"[FarGrab] Minimum tutus mesafesi: {minimumDistance:F2} m", this);
    }

    private void OnDisable()
    {
        foreach (Binding binding in bindings)
        {
            if (binding.Controller != null)
                binding.Controller.attachUpdated -= binding.Callback;
        }

        bindings.Clear();
    }

    private void BindInteractors()
    {
        OnDisable();

        NearFarInteractor[] interactors = FindObjectsByType<NearFarInteractor>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (NearFarInteractor interactor in interactors)
        {
            if (interactor.interactionAttachController is not InteractionAttachController controller)
                continue;

            Binding binding = new(interactor, controller);
            binding.Callback = () => ClampDistance(binding);
            controller.attachUpdated += binding.Callback;
            bindings.Add(binding);
        }
    }

    private void ClampDistance(Binding binding)
    {
        if (!binding.Interactor.hasSelection)
        {
            binding.WasFarSelection = false;
            return;
        }

        Transform follow = binding.Controller.transformToFollow;
        IInteractionAttachController attachController = binding.Controller;
        Transform anchor = attachController.GetOrCreateAnchorTransform();
        if (follow == null || anchor == null)
            return;

        Vector3 offset = anchor.position - follow.position;
        float distance = offset.magnitude;

        if (binding.Controller.hasOffset)
            binding.WasFarSelection = true;

        if (!binding.WasFarSelection || distance >= minimumDistance)
            return;

        // Cok yakinda eski tutus acisini korumak, kumanda dondugunde isin
        // bukulmesine ve nesnenin tekrar uzaklastirilirken yana kacmasina yol
        // aciyordu. Minimum mesafede ekseni kumandanin guncel ileri yonune
        // yeniden hizala; bundan sonraki itme/cekme hareketi duz devam eder.
        attachController.MoveTo(follow.position + follow.forward * minimumDistance);
    }

    private sealed class Binding
    {
        public readonly NearFarInteractor Interactor;
        public readonly InteractionAttachController Controller;
        public Action Callback;
        public bool WasFarSelection;

        public Binding(NearFarInteractor interactor, InteractionAttachController controller)
        {
            Interactor = interactor;
            Controller = controller;
        }
    }
}
