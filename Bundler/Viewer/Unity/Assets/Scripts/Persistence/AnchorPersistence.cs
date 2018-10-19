// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using System;

#if UNITY_WSA
using UnityEngine.XR.WSA.Persistence;
using UnityEngine.XR.WSA;
using UnityEngine.XR.WSA.Sharing;
#endif


namespace Persistence
{
    public enum AnchorPersistenceEventType
    {
        /// <summary>
        /// An error state
        /// </summary>
        Unknown,

        /// <summary>
        /// Atttempting to apply a shared anchor
        /// </summary>
        ApplyingShared,

        /// <summary>
        /// Successfully applied a shared anchor
        /// </summary>
        AppliedShared,

        /// <summary>
        /// An anchor was loaded from cached
        /// </summary>
        Loaded,

        /// <summary>
        /// An anchor was saved to the cache
        /// </summary>
        Saved,

        /// <summary>
        /// An anchor is about to be placed on object
        /// </summary>
        Placing,

        /// <summary>
        /// An anchor is about to be placed on object
        /// </summary>
        Placed
    }

    /// <summary>
    /// Holds data on the presistence event
    /// </summary>
    public class AnchorPersistenceEventArgs
    {
        /// <summary>
        /// The type of the persistence event
        /// </summary>
        public AnchorPersistenceEventType Type { get; set; }

        /// <summary>
        /// The anchor id
        /// </summary>
        public string AnchorId { get; set; }


        /// <summary>
        /// The game object that owns this anchor
        /// </summary>
        public GameObject AnchorOwner { get; set; }
    }

    /// <summary>
    /// Loads or saves an anchor with this gameobject name.
    /// </summary>
    public class AnchorPersistence : MonoBehaviour
    {
        /// <summary>
        /// Invoked when this object finished an action, like save or load.
        /// </summary>
        public delegate void OnAnchorPersistenceEvent(AnchorPersistence source, AnchorPersistenceEventArgs args);
        public event OnAnchorPersistenceEvent AnchorPersistenceEvent;

        /// <summary>
        /// Invoked when the persitence behavior loads an anchor from cache
        /// </summary>
        public event Action AnchorLoaded;

        [HideInInspector]
        public GameObject TargetGameObject = null;

        public bool AutoLoadOnEnable = true;
        public bool IsReady { get; private set; }

        private bool _isAnchored;
        public bool IsAnchored
        {
            get
            {
                return _isAnchored;
            }
        }
#if UNITY_WSA
        private WorldAnchorStore worldAnchorStore;
#endif
        private PersistenceSaveLoad saveLoad;

        void OnEnable()
        {
            if (saveLoad == null)
            {
                saveLoad = ScriptableObject.CreateInstance("PersistenceSaveLoad") as PersistenceSaveLoad;
                saveLoad.PersistenceEvent += SaveLoad_PersistenceEvent;
            }
#if UNITY_WSA
            if (worldAnchorStore == null)
            {
                IsReady = false;
                WorldAnchorStore.GetAsync(delegate (WorldAnchorStore store)
                {
                    this.worldAnchorStore = store;
                    IsReady = true;
                    if (AutoLoadOnEnable)
                    {
                        LoadAnchor();
                    }
                });
            }
            else
            {
                if (AutoLoadOnEnable)
                {
                    LoadAnchor();
                }
            }
#endif
        }

        public bool LoadAnchor()
        {
            TargetGameObject = TargetGameObject == null ? gameObject : TargetGameObject;

#if UNITY_WSA
            _isAnchored = saveLoad.LoadLocation(TargetGameObject, worldAnchorStore);
#endif
            if (!_isAnchored)
            {
                Debug.Log("No saved anchors found for zone " + TargetGameObject.name + ", align and save anchors first");
                
            }
            if (_isAnchored && AnchorLoaded != null)
            {
                AnchorLoaded();
            }
            return _isAnchored;
        }

        public bool ClearAnchor(bool removeSavedLocation)
        {
            _isAnchored = false;
            TargetGameObject = TargetGameObject == null ? gameObject : TargetGameObject;

#if UNITY_WSA
            if (removeSavedLocation)
            {
                return saveLoad.DeleteLocation(TargetGameObject, worldAnchorStore);
            }

            if (TargetGameObject.GetComponent<WorldAnchor>() != null)
            {
                DestroyImmediate(TargetGameObject.GetComponent<WorldAnchor>());
            }
#endif
            return true;
        }

        public Guid PlaceAnchor(bool saveAchor)
        {
            TargetGameObject = TargetGameObject == null ? gameObject : TargetGameObject;
#if UNITY_WSA
            ClearAnchor(false);

            // create a new anchor id for saving and sharing
            Guid anchorId = Guid.NewGuid();
            var storageIdString = anchorId.ToString();

            // Notify other that an anchor is about to be placed
            RaiseAnchorPersistenceEvent(AnchorPersistenceEventType.Placing, TargetGameObject, storageIdString);

            // Apply anchor
            TargetGameObject.AddComponent<WorldAnchor>();
            _isAnchored = true;

            // Notify others that an anchor was placed
            RaiseAnchorPersistenceEvent(AnchorPersistenceEventType.Placed, TargetGameObject, storageIdString);

            if (saveAchor)
            {
                saveLoad.SaveLocation(anchorId, TargetGameObject, worldAnchorStore);
            }
#endif
            return anchorId;
        }

        /// <summary>
        /// Given a transfer batch, apply only the first anchor id to this object's gameObject.
        /// </summary>
        /// <param name="batch"></param>
        /// <returns>True if gameObject is anchored</returns>
        public bool ApplyAnchor(WorldAnchorTransferBatch batch, bool saveAchor)
        {
#if UNITY_WSA
            TargetGameObject = TargetGameObject == null ? gameObject : TargetGameObject;

            if (saveAchor)
            {
                _isAnchored = saveLoad.ApplySharedLocation(TargetGameObject, batch, worldAnchorStore);
            }
            else
            {
                ClearAnchor(false);
                var batchIds = batch.GetAllIds();
                if (batchIds.Length > 0)
                {
                    batch.LockObject(batchIds[0], TargetGameObject);
                    _isAnchored = true;
                }
                else
                {
                    _isAnchored = false;
                }
            }
#endif

            return _isAnchored;
        }

        /// <summary>
        /// Re-boardcast the "saveLoad" events to this object's listeners
        /// </summary>
        private void SaveLoad_PersistenceEvent(PersistenceSaveLoad source, PersistenceEventArgs args)
        {
            AnchorPersistenceEventType type = AnchorPersistenceEventType.Unknown;

            switch (args.Type)
            {
                case PersistenceEventType.AppliedShared:
                    type = AnchorPersistenceEventType.AppliedShared;
                    break;

                case PersistenceEventType.ApplyingShared:
                    type = AnchorPersistenceEventType.ApplyingShared;
                    break;

                case PersistenceEventType.Loaded:
                    type = AnchorPersistenceEventType.Loaded;
                    break;

                case PersistenceEventType.Saved:
                    type = AnchorPersistenceEventType.Saved;
                    break;
            }

            if (type != AnchorPersistenceEventType.Unknown)
            {
                RaiseAnchorPersistenceEvent(type, args.AnchorOwner, args.AnchorId);
            }
        }

        private void RaiseAnchorPersistenceEvent(AnchorPersistenceEventType type, GameObject owner, string anchorId)
        {
            Debug.LogFormat("[AnchorPersistence] RaiseAnchorPersistenceEvent (type: {0}) (anchor id: {1})", type, anchorId);
            if (AnchorPersistenceEvent != null)
            {
                AnchorPersistenceEventArgs args = new AnchorPersistenceEventArgs();
                args.AnchorId = anchorId;
                args.AnchorOwner = owner;
                args.Type = type;

                if (AnchorPersistenceEvent != null)
                {
                    AnchorPersistenceEvent(this, args);
                }
            }
        }
    }
}