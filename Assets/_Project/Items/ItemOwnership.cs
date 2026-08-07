using Alteruna.Multiplayer.Unity;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class ItemOwnership : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private RigidbodySynchronizable synchronizedBody;

    private void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        synchronizedBody = GetComponent<RigidbodySynchronizable>();
    }

    private void OnEnable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
        }
    }

    private void OnDisable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
            grabInteractable.selectExited.RemoveListener(OnSelectExited);
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (synchronizedBody == null)
            return;

        // Alteruna RigidbodySynchronizable uses the sender of the next soft update as
        // the current physics owner. A grab therefore takes authority without freezing
        // the other client or maintaining a second custom ownership system.
        synchronizedBody.AllowCollisionToAssumeOwner = true;
        synchronizedBody.SendData = true;
        synchronizedBody.SyncSettings();
        synchronizedBody.ForceUpdate();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (synchronizedBody == null)
            return;

        synchronizedBody.SyncSettings();
        synchronizedBody.ForceUpdate();
    }
}
