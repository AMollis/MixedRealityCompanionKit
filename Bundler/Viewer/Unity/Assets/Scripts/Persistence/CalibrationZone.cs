// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using System;
using System.Collections;
using UnityEngine.XR.WSA.Sharing;

namespace Persistence
{
    [RequireComponent(typeof(NetworkAnchor))]
    [RequireComponent(typeof(AnchorPersistence))]
    public class CalibrationZone : MonoBehaviour
    {
        public event Action<CalibrationZone> CalibrationStarted;
        public event Action<CalibrationZone> CalibrationComplete;

        [Tooltip("Root transform to align. Leave null to use current transform.")]
        public Transform TargetTransform = null;

        [Tooltip("Root transform to align as a preview. Once previewing is complete, position will be migrated to the target transform. Leave null to use the target transform.")]
        public Transform PreviewTransform = null;

        [HideInInspector]
        public bool IsCalibrated { get { return persistence.IsAnchored; } }
        [HideInInspector]
        public bool IsCalibrating = false;

        private AnchorPersistence persistence;
        private NetworkAnchor networkAnchor;
        
        void Awake()
        {
            TargetTransform = TargetTransform == null ? gameObject.transform : TargetTransform;
            PreviewTransform = PreviewTransform == null ? TargetTransform : PreviewTransform;
            ResetPreviewTransform();
        }

        void Start()
        {
            networkAnchor = GetComponent<NetworkAnchor>();
            networkAnchor.ReceivedRemoteAnchorTransferBatch += NetworkAnchor_ReceivedRemoteAnchorTransferBatch;

            persistence = GetComponent<AnchorPersistence>();
            persistence.TargetGameObject = TargetTransform.gameObject;
            persistence.AnchorPersistenceEvent += Persistence_AnchorPersistenceEvent;
        }

        private void NetworkAnchor_ReceivedRemoteAnchorTransferBatch(NetworkAnchor sender, WorldAnchorTransferBatch batch)
        {
            persistence.ApplyAnchor(batch, true);
        }

        private void Persistence_AnchorPersistenceEvent(AnchorPersistence source, AnchorPersistenceEventArgs args)
        {
            if (args.Type == AnchorPersistenceEventType.Loaded)
            {
                StartCoroutine(networkAnchor.SetDefaultAnchor(args.AnchorId, args.AnchorOwner));
            }
        }

        public void AlignZone()
        {
            if (IsCalibrating)
            {
                Debug.Log("AlignZone failed - already calibrating: " + this.name);
                return;
            }

            IsCalibrating = true;
            Debug.Log("AlignZone: " + this.name);
            
            ClearAnchor(false);
            if (this.CalibrationStarted != null)
            {
                this.CalibrationStarted.Invoke(this);
            }
        }

        public void LockZone(bool placeAnchor, bool saveAnchor)
        {
            if (!IsCalibrating)
            {
                Debug.Log("LockZone failed - not calibrating: " + this.name);
                return;
            }

            Debug.Log("LockZone: " + this.name);

            if (placeAnchor)
            {
                PlaceAnchor(saveAnchor);
            }
            else
            {
                CommitPreviewTransform();
            }

            IsCalibrating = false;

            if (this.CalibrationComplete != null)
            {
                this.CalibrationComplete.Invoke(this);
            }
        }

        public void PlaceAnchor(bool saveAnchor)
        {
            CommitPreviewTransform();
            string anchorId = persistence.PlaceAnchor(saveAnchor).ToString();
            networkAnchor.CheckinAnchor(anchorId, TargetTransform.gameObject);
        }

        private void CommitPreviewTransform()
        {
            if (PreviewTransform != TargetTransform)
            {
                TargetTransform.position = PreviewTransform.position;
                TargetTransform.rotation = PreviewTransform.rotation;
                TargetTransform.localScale = PreviewTransform.localScale;
                ResetPreviewTransform();
            }
        }

        public delegate void ClearAnchorResult(bool success);

        public IEnumerator ClearAnchorAsync(bool removeSavedLocation, ClearAnchorResult callback)
        {
            yield return networkAnchor.CheckoutAnchorAsync(persistence.TargetGameObject);
            if (networkAnchor.CheckedOutAnchor)
            {
                ClearAnchor(removeSavedLocation);
            }
            callback(networkAnchor.CheckedOutAnchor);
        }

        private bool ClearAnchor(bool removeSavedLocation)
        {
            var ret = persistence.ClearAnchor(removeSavedLocation);
            return ret;
        }

        private void ResetPreviewTransform()
        {
            if (TargetTransform != PreviewTransform)
            {
                PreviewTransform.localPosition = Vector3.zero;
                PreviewTransform.localRotation = Quaternion.identity;
                PreviewTransform.localScale = Vector3.one;
            }
        }
    }
}