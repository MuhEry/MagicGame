using Alteruna.Multiplayer;
using UnityEngine;
using Alteruna.Multiplayer.Unity;
using UnityEngine.XR.Interaction.Toolkit;

namespace Alteruna
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable), typeof(RigidbodySynchronizable))]
	public class XRGrabInteractableSync : MonoBehaviour
	{
		private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable _grabInteractable;
		private RigidbodySynchronizable _rigidbody;

		private void Start()
		{
			_grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
			_rigidbody = GetComponent<RigidbodySynchronizable>();

			_grabInteractable.selectEntered.AddListener(OnSelectEntered);
			_grabInteractable.selectExited.AddListener(OnSelectExited);
		}

		private void OnSelectExited(SelectExitEventArgs arg0)
		{
			_rigidbody.SyncSettings();
		}

		private void OnSelectEntered(SelectEnterEventArgs arg0)
		{
			_rigidbody.SyncSettings();
		}
	}
}
