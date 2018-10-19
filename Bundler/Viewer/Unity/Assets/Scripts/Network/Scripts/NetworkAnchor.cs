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
    /// For this to function, there must be a global NetworkAnchorManager.
    /// </summary>
    private NetworkAnchorManager networkAnchorManager;

    /// <summary>
    /// For this to function, the game object must also have a local instance of an NetworkAnchorPlayer.
    /// </summary>
    private NetworkAnchorPlayer anchorPlayer;

    /// <summary>
    /// The last position of the game object
    /// </summary>
    private Vector3 checkoutPosition;

    /// <summary>
    /// The last received anchor id. Needed to move this anchor later.
    /// </summary>
    private string lastReceivedAnchorId;

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
        WhenReadyInitializeAnchorManagerOnce();
        WhenReadyInitializeAnchorPlayerOnce();
        UpdateActiveGameObjects();
        UpdateAnchorPositions();
    }

    /// <summary>
    /// Check if we can inititialize the anchor manager usage. If we can, only do the initialization work once. Note
    /// that the anchor manager instance won't be ready at "Start".
    /// </summary>
    private void WhenReadyInitializeAnchorManagerOnce()
    {
        // Check if already initialized
        if (networkAnchorManager != null)
        {
            return;
        }

        // Check if can initialize
        networkAnchorManager = NetworkAnchorManager.Instance;
        if (networkAnchorManager == null)
        {
            return;
        }

        networkAnchorManager.ImportedAnchorChanged += OnReceivedRemoteAnchor;
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

        // If an anchor blob was received from another player, now's the time to handle this.
        if (networkAnchorManager != null)
        {
            OnReceivedRemoteAnchor(networkAnchorManager, networkAnchorManager.ImportedAnchor);
        }
        else
        {
            Debug.LogError("[NetworkAnchor] This object can't function correctly when there isn't a Network Anchor Manager.");
        }
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
    public IEnumerator CheckoutAnchorAsync(GameObject target)
    {
        while (anchorPlayer == null)
        {
            yield return null;
        }

        Debug.LogFormat("[NetworkAnchor] Checking out anchor.");
        yield return anchorPlayer.CheckoutAnchorAsync();
        checkoutPosition = target.transform.position;
        Debug.LogFormat("[NetworkAnchor] Check-out anchor. (checked out: {0})", CheckedOutAnchor);
    }

    /// <summary>
    /// Checkin a new anchor to the server.
    /// </summary>
    /// <param name="anchorId">The id of the anchor to share</param>
    /// <param name="target">The game object that owns the anchor</param>
    public void CheckinAnchor(string anchorId, GameObject target)
    {
        if (anchorPlayer != null)
        {
            MoveAnchor(target.transform.position - checkoutPosition);
            checkoutPosition = Vector3.zero;

            Debug.LogFormat("[NetworkAnchor] Checking in new anchor. (anchorId = {0})", anchorId);
            anchorPlayer.CheckInAnchor(anchorId, target);
        }
        else
        {
            Debug.LogFormat("[NetworkAnchor] Failed to check in new anchor, since there was no player. (anchorId = {0})", anchorId);
        }
    }

    /// <summary>
    /// Move the last shared achor by a given offset.
    /// </summary>
    /// <param name="offset">The position offset</param>
    private void MoveAnchor(Vector3 offset)
    {
        if (anchorPlayer != null && !string.IsNullOrEmpty(lastReceivedAnchorId))
        {
            Debug.LogFormat("[NetworkAnchor] Moving last shared anchor. (moveOffset = {0})", offset);
            anchorPlayer.MoveAnchor(lastReceivedAnchorId, offset);
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
        // Check if we've recieved an anchor manager yet.
        if (networkAnchorManager == null)
        {
            return;
        }

        if (LoadingAnchorRoot != null && networkAnchorManager.ImportingAnchor)
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
        // If this doesn't have the anchor checked out, update the anchor's offset.
        if (ImportedAnchorOffset != null && networkAnchorManager != null && !CheckedOutAnchor)
        {
            ImportedAnchorOffset.transform.localPosition = networkAnchorManager.AnchorOffset;
        }
    }

    /// <summary>
    /// When receiving a remote anchor, notify other components. Once of these compoents should apply the new anchor
    /// </summary>
    private void OnReceivedRemoteAnchor(NetworkAnchorManager sender, ImportedAnchorChangedArgs args)
    {
        if (args == null)
        {
            return;
        }

        lastReceivedAnchorId = args.AnchorId;
        if (ReceivedRemoteAnchorTransferBatch != null)
        {
            ReceivedRemoteAnchorTransferBatch(this, args.TransferBatch);
        }
    }
}
