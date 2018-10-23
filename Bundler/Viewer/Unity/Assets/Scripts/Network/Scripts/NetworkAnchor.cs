// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using Persistence;
using System.Collections;
using UnityEngine.XR.WSA.Sharing;

public class NetworkAnchor : MonoBehaviour
{
    [Tooltip("The game object to enable when the initial network anchor is being loaded.")]
    public GameObject LoadingAnchorRoot;

    [Tooltip("The game object to enable when the initial network anchor has been loaded.")]
    public GameObject FoundAnchorRoot;

    [Tooltip("The game object to move while the imported anchor moves, before a new anchor arrives.")]
    public GameObject ImportedAnchorOffset;

    /// <summary>
    /// Get if this currently has the anchor checked out.
    /// </summary>
    public bool CheckedOutAnchor
    {
        get
        {
            return anchorPlayer != null && anchorPlayer.CheckedOutAnchor;
        }
    }

    public delegate void OnReceivedRemoteAnchorTransferBatch(NetworkAnchor sender, WorldAnchorTransferBatch batch);

    /// <summary>
    /// Event raised when a new remote transf batch has arrived.
    /// </summary>
    public event OnReceivedRemoteAnchorTransferBatch ReceivedRemoteAnchorTransferBatch;

    /// <summary>
    /// For this to function, the game object must also have a local instance of an NetworkAnchorPlayer.
    /// </summary>
    private NetworkAnchorPlayer anchorPlayer;

    /// <summary>
    /// The last position of the game object, when checked-out
    /// </summary>
    private Matrix4x4 worldToLocalMatrixAtCheckOutTime = Matrix4x4.identity;

    /// <summary>
    /// The last received anchor id. Needed to move this anchor later.
    /// </summary>
    private string lastKnownAnchorId;

    /// <summary>
    /// Used for debugging only
    /// </summary>
    private Vector3 lastAppliedAnchorPosition;

    /// <summary>
    /// Used for debugging only
    /// </summary>
    private Quaternion lastAppliedAnchorRotation;

    private void Awake()
    {
        if (FoundAnchorRoot != null)
        {
            FoundAnchorRoot.SetActive(false);
        }

        if (LoadingAnchorRoot != null)
        {
            LoadingAnchorRoot.SetActive(false);
        }
    }

    /// <summary>
    /// Update anchor state.
    /// </summary>
    private void Update()
    {
        WhenReadyInitializeAnchorPlayerOnce();
        UpdateActiveGameObjects();
        UpdateAnchorPositions();
    }

    /// <summary>
    /// Check if we can inititialize the local instance of an anchor player. If we can, only do the initialization work
    /// once. Note that the local player may not exist at "Start".
    /// </summary>
    private void WhenReadyInitializeAnchorPlayerOnce()
    {
        // Check if already initialized
        if (anchorPlayer != null)
        {
            return;
        }

        // Check if can initialize
        anchorPlayer = NetworkAnchorPlayer.LocalInstance;
        if (anchorPlayer == null)
        {
            return;
        }

        anchorPlayer.ImportedAnchorChanged += OnReceivedRemoteAnchor;
        anchorPlayer.ExportedAnchor += OnSharedRemoteAnchor;
        OnReceivedRemoteAnchor(anchorPlayer, anchorPlayer.ImportedAnchor);
    }

    /// <summary>
    /// Set the default shared network anchor, if one hasn't be set yet.
    /// </summary>
    /// <param name="anchorId">The id of the anchor to share</param>
    /// <param name="target">The game object that owns the anchor</param>
    public IEnumerator SetDefaultAnchor(string anchorId, GameObject target)
    {
        while (anchorPlayer == null)
        {
            yield return null;
        }

        Debug.LogFormat("[NetworkAnchor] Setting default anchor.");
        yield return anchorPlayer.SetDefaultNetworkAnchorAsync(anchorId, target);
    }

    /// <summary>
    /// Checkout the network anchor for editting
    /// </summary>
    /// <param name="target">The game object that will modify anchor</param>
    public IEnumerator CheckOutAnchorAsync(GameObject target)
    {
        while (anchorPlayer == null)
        {
            yield return null;
        }

        Debug.LogFormat("[NetworkAnchor] Checking out anchor.");
        yield return anchorPlayer.CheckOutAnchorAsync();
        worldToLocalMatrixAtCheckOutTime = target.transform.worldToLocalMatrix;
        Debug.LogFormat("[NetworkAnchor] Check-out anchor. (checked out: {0})", CheckedOutAnchor);
        UpdateAnchorPositions();
    }

    /// <summary>
    /// Checkin a new anchor to the server.
    /// </summary>
    /// <param name="anchorId">The id of the anchor to share</param>
    /// <param name="target">The game object that owns the anchor</param>
    public void CheckInAnchor(string anchorId, GameObject target)
    {
        if (anchorPlayer != null)
        {
            // Move the last anchor by the amount the target was moved
            if (!worldToLocalMatrixAtCheckOutTime.isIdentity)
            {
                var moveDelta = worldToLocalMatrixAtCheckOutTime.MultiplyPoint3x4(target.transform.position);
                var rotateDelta = (worldToLocalMatrixAtCheckOutTime * target.transform.worldToLocalMatrix.inverse).rotation.eulerAngles;
                MoveAnchor(moveDelta, rotateDelta);
            }
            worldToLocalMatrixAtCheckOutTime = Matrix4x4.identity;

            Debug.LogFormat("[NetworkAnchor] Checking in new anchor. (anchorId: {0})", anchorId);
            anchorPlayer.CheckInAnchor(anchorId, target);
        }
        else
        {
            Debug.LogFormat("[NetworkAnchor] Failed to check in new anchor, since there was no player. (anchorId: {0})", anchorId);
        }
    }

    /// <summary>
    /// Move the last shared achor by a given offset.
    /// </summary>
    /// <param name="offset">The position offset</param>
    private void MoveAnchor(Vector3 positionDelta, Vector3 eulerAnglesDelta)
    {
        if (anchorPlayer != null && !string.IsNullOrEmpty(lastKnownAnchorId))
        {
            Debug.LogFormat("[NetworkAnchor] Moving last shared anchor. (lastKnownAnchorId: {0}) (positionDelta: {1}) (eulerAnglesDelta: {2})", lastKnownAnchorId, positionDelta, eulerAnglesDelta);
            anchorPlayer.MoveAnchor(lastKnownAnchorId, positionDelta, eulerAnglesDelta);
        }
        else
        {
            Debug.LogError("[NetworkAnchor] Failed to move last shared anchor, since there was no player or no last received anchor");
        }
    }

    /// <summary>
    /// Update "root" object based on if anchors are being downloaded.
    /// </summary>
    private void UpdateActiveGameObjects()
    {
        // Check if we've recieved an anchor player yet.
        if (anchorPlayer == null)
        {
            return;
        }

        if (LoadingAnchorRoot != null && anchorPlayer.ImportingAnchor)
        {
            if (FoundAnchorRoot != null)
            {
                FoundAnchorRoot.SetActive(false);
            }

            LoadingAnchorRoot.SetActive(true);
        }
        else
        {
            // Only show "loading anchor root" once
            if (LoadingAnchorRoot != null)
            {
                LoadingAnchorRoot.SetActive(false);
                LoadingAnchorRoot = null;
            }

            // Show "found anchor root", if not loading or there is no "loading anchor root"
            if (FoundAnchorRoot != null)
            {
                FoundAnchorRoot.SetActive(true);
            }
        }
    }

    /// <summary>
    /// Notify the anchor player that this anchor as moved
    /// </summary>
    private void UpdateAnchorPositions()
    {
        if (ImportedAnchorOffset == null)
        {
            return;
        }

        // If this doesn't have the anchor checked out, update the anchor's offset.
        if (anchorPlayer != null && !CheckedOutAnchor && !string.IsNullOrEmpty(lastKnownAnchorId))
        {
            anchorPlayer.ApplyMovement(lastKnownAnchorId, ImportedAnchorOffset);
        }
        else
        {
            ImportedAnchorOffset.transform.localPosition = Vector3.zero;
            ImportedAnchorOffset.transform.localRotation = Quaternion.identity;
        }

        if (ImportedAnchorOffset.transform.localPosition != lastAppliedAnchorPosition ||
            ImportedAnchorOffset.transform.localRotation != lastAppliedAnchorRotation)
        {
            lastAppliedAnchorPosition = ImportedAnchorOffset.transform.localPosition;
            lastAppliedAnchorRotation = ImportedAnchorOffset.transform.localRotation;
            Debug.LogFormat("[NetworkAnchor] Applied movement to anchor. (lastKnownAnchorId: {0}) (lastAppliedAnchorPosition: {1}) (lastAppliedAnchorRotation: {2})", lastKnownAnchorId, lastAppliedAnchorPosition, lastAppliedAnchorRotation);
        }
    }

    /// <summary>
    /// When receiving a remote anchor, notify other components. Once of these compoents should apply the new anchor
    /// </summary>
    private void OnReceivedRemoteAnchor(NetworkAnchorPlayer sender, ImportedAnchorChangedArgs args)
    {
        if (args == null)
        {
            return;
        }

        Debug.LogFormat("[NetworkAnchor] Received a new anchor from a remote client. (anchorId: {0})", args.AnchorId);
        lastKnownAnchorId = args.AnchorId;
        if (ReceivedRemoteAnchorTransferBatch != null)
        {
            ReceivedRemoteAnchorTransferBatch(this, args.TransferBatch);
        }
    }

    /// <summary>
    /// Called when the local player has successfully exported and shared an anchor.
    /// </summary>
    private void OnSharedRemoteAnchor(NetworkAnchorPlayer sender, string anchorId)
    {
        Debug.LogFormat("[NetworkAnchor] Finished check-in and now sharing a new anchor. (anchorId: {0})", anchorId);
        lastKnownAnchorId = anchorId;
    }
}
