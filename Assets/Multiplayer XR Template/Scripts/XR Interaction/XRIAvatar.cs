using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;


namespace Alteruna
{
	public class XRIAvatar : CommunicationBridge
	{
		private const string XRI_NAMESPACE = "UnityEngine.XR.Interaction.Toolkit";
		private const string XR_CORE_NAMESPACE = "Unity.XR.CoreUtils";

		private static readonly Type[] _componentsToRemove =
		{
			typeof(TrackedPoseDriver),
			typeof(CharacterController),
			typeof(Camera),
			typeof(AudioListener)
		};

		public override void Possessed(bool isMe, User user)
		{
			if (isMe)
			{
				SetLocalOutputEnabled(true);
				StartCoroutine(CharacterControllerFix());
				return;
			}

			// Uzak oyuncunun Camera/AudioListener'i Destroy'un frame sonunu
			// beklemeden kapanmali; aksi halde bir kare bile XR cikisini ve ana
			// AudioListener'i ele gecirebilir. Prefab child sirasina guvenme.
			SetLocalOutputEnabled(false);
			RemoveComponents();
			StartCoroutine(DestroyEmptyChildrenNextFrame());
		}

		private void SetLocalOutputEnabled(bool enabled)
		{
			foreach (var camera in GetComponentsInChildren<Camera>(true))
				camera.enabled = enabled;

			foreach (var listener in GetComponentsInChildren<AudioListener>(true))
				listener.enabled = enabled;
		}

		private void RemoveComponents()
		{
			var components = GetComponentsInChildren<Behaviour>(true);
			var toDestroy = new List<Behaviour>();

			foreach (var component in components)
			{
				var type = component.GetType();

				// ignore Transform
				if (type == typeof(Transform)) continue;

				// Destroy Vignette Controller objects
				if (type == typeof(UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort.TunnelingVignetteController))
				{
					Destroy(component.gameObject);
					continue;
				}

				// have to destroy Event Systems one last.
				if (type == typeof(EventSystem))
				{
					toDestroy.Add(component);
					continue;
				}

				// if matched any from _componentsToRemove, destroy it.
				if (ComplementMatch(component, _componentsToRemove))
				{
					Destroy(component);
					continue;
				}

				var name = type.Namespace ?? string.Empty;
				if (name.Length < XRI_NAMESPACE.Length)
				{
					if (name == XR_CORE_NAMESPACE) Destroy(component);
				}
				else
				{
					name = name.Substring(0, XRI_NAMESPACE.Length);
					if (name == XRI_NAMESPACE) Destroy(component);
				}
			}

			// destroy remaining components
			foreach (Component compoment in toDestroy) Destroy(compoment);
		}

		private static bool ComplementMatch(Component component, Type[] match) =>
			match.Any(type => component.GetType() == type);

		private IEnumerator DestroyEmptyChildrenNextFrame()
		{
			var toDestroy = new List<Transform>();
			DestroyEmptyChildren(transform, ref toDestroy);
			yield return null;
			foreach (var obj in toDestroy)
				if (obj != null)
					Destroy(obj.gameObject);
			yield return null;
			toDestroy.Clear();
			DestroyEmptyChildren(transform, ref toDestroy, false);
			foreach (var obj in toDestroy)
				if (obj != null)
					Destroy(obj.gameObject);
		}

		private IEnumerator CharacterControllerFix()
		{
			// Wait until next frame
			yield return null;
			var cc = GetComponent<CharacterController>();
			if (cc != null)
				cc.center = new Vector3(0, cc.center.y, 0);
		}

		private bool DestroyEmptyChildren(Transform transform, ref List<Transform> toDestroy, bool enableObjs = true)
		{
			var hasChildren = false;
			foreach (Transform child in transform)
				if (!DestroyEmptyChildren(child, ref toDestroy))
					hasChildren = true;

			if (!hasChildren && transform.GetComponents<Component>().Length == 1)
			{
				toDestroy.Add(transform);
				return true;
			}

			if (enableObjs) transform.gameObject.SetActive(true);
			return false;
		}
	}
}
