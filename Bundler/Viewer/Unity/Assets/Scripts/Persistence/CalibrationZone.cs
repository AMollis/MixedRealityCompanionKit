// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using System;

namespace Persistence
{
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

        private struct OriginalTransform
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private OriginalTransform originalTarget;
        private AnchorPersistence persistence;
        
        void Awake()
        {
            TargetTransform = TargetTransform == null ? gameObject.transform : TargetTransform;
            PreviewTransform = PreviewTransform == null ? TargetTransform : PreviewTransform;

            originalTarget.position = TargetTransform.localPosition;
            originalTarget.rotation = TargetTransform.localRotation;
            originalTarget.scale = TargetTransform.localScale;

            ResetPreviewTransform();
        }

        void Start()
        {
            persistence = GetComponent<AnchorPersistence>();
            persistence.TargetGameObject = TargetTransform.gameObject;
            persistence.AnchorLoaded += Persistence_AnchorLoaded;

            Persistence_AnchorLoaded();
        }

        private void Persistence_AnchorLoaded()
        {
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
            persistence.PlaceAnchor(saveAnchor);
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

        public bool ClearAnchor(bool removeSavedLocation)
        {
            var ret =  persistence.ClearAnchor(removeSavedLocation);
            Persistence_AnchorLoaded();
            return ret;
        }

        public void ResetTransform(bool removeSavedLocation = false)
        {
            ClearAnchor(removeSavedLocation);
            TargetTransform.position = originalTarget.position;
            TargetTransform.rotation = originalTarget.rotation;
            TargetTransform.localScale = originalTarget.scale;
            ResetPreviewTransform();
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