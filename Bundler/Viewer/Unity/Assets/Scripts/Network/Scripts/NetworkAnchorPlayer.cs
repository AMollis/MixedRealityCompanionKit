// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

public class NetworkAnchorPlayer : NetworkBehaviour
{
    /// <summary>
    /// The current check-out request for the shared anchor
    /// </summary>
    CheckoutRequest currentRequest = new CheckoutRequest(0);

    /// <summary>
    /// A lock to protect currentRequest
    /// </summary>
    object currentRequestLock = new object();

    /// <summary>
    ///  The local anchor player. 
    /// </summary>
    public static NetworkAnchorPlayer LocalInstance { get; private set; }

    /// <summary>
    /// For this to function, there must be a global NetworkAnchorManager.
    /// </summary>
    private NetworkAnchorManager networkAnchorManager;

    /// <summary>
    /// This event is raised when a new anchor arrives from a different player.
    /// </summary>
    /// <param name="args">Contains the data that arrived.</param>
    public delegate void OnImportedAnchorChanged(NetworkAnchorPlayer sender, ImportedAnchorChangedArgs args);
    public event OnImportedAnchorChanged ImportedAnchorChanged;

    /// <summary>
    /// An event raised when this player has exported an anchor. The event will return the exported anchor id
    /// </summary>
    public delegate void OnExportedAnchor(NetworkAnchorPlayer sender, string exportedAnchorId);
    public event OnExportedAnchor ExportedAnchor;

    /// <summary>
    /// Get the last received remote anchor
    /// </summary>
    public ImportedAnchorChangedArgs ImportedAnchor
    {
        get
        {
            if (networkAnchorManager != null)
            {
                return networkAnchorManager.ImportedAnchor;
            }
            else
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Get if the local anchor player is in the process of receiving a shared anchor.
    /// </summary>
    public bool ImportingAnchor
    {
        get
        {
            if (networkAnchorManager != null)
            {
                return networkAnchorManager.ImportingAnchor;
            }
            else
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Get if this player currently has the anchor checked out.
    /// </summary>
    public bool CheckedOutAnchor
    {
        get
        {
            return checkedOutAnchorCount > 0;
        }
    }

    /// <summary>
    /// The number of active check out requests. A client could check out more than once if an anchor is currently being exported.
    /// </summary>
    private int checkedOutAnchorCount;
    private object checkedOutAnchorCountLock = new object();

    /// <summary>
    /// Prevent client from being destroyed on scene changes.
    /// </summary>
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Update anchor player state.
    /// </summary>
    private void Update()
    {
        WhenReadyInitializeAnchorManagerOnce();
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

        networkAnchorManager.ImportedAnchorChanged += NetworkAnchorManager_ImportedAnchorChanged;
    }


    /// <summary>
    /// Create a string with debug information.
    /// </summary>
    private string DebugInfo()
    {
        string clientConnectionIp = connectionToClient == null ? "not server" : connectionToClient.address;
        return string.Format("(netId: {0}) (isLocalPlayer: {1}) (isServer: {2}) (isClient: {3}) (hasAuthority: {4}) (connection to client: {5})",
            netId,
            isLocalPlayer,
            isServer,
            isClient,
            hasAuthority,
            clientConnectionIp);
    }

    /// <summary>
    /// This is invoked on the behaviour that have UNet authority
    /// </summary>
    public override void OnStartAuthority()
    {
        base.OnStartAuthority();

        if (hasAuthority)
        {
            LocalInstance = this;
        }
    }

    /// <summary>
    /// Handle network object being destroyed
    /// </summary>
    public override void OnNetworkDestroy()
    {
        base.OnNetworkDestroy();

        // Check-in anchor, just in case this was the anchor owner
        if (this.isServer)
        {
            CheckInAnchor(SharedAnchorData.Empty);
        }
    }

    /// <summary>
    /// When receiving a remote anchor, notify other components. Once of these compoents should apply the new anchor
    /// </summary>
    private void NetworkAnchorManager_ImportedAnchorChanged(NetworkAnchorManager sender, ImportedAnchorChangedArgs args)
    {
        if (ImportedAnchorChanged != null)
        {
            ImportedAnchorChanged(this, args);
        }
    }

    /// <summary>
    /// Apply the anchor movements to the given transform
    /// </summary>
    public void ApplyMovement(string anchorId, GameObject target)
    {
        if (networkAnchorManager != null)
        {
            networkAnchorManager.ApplyMovement(anchorId, target);
        }
    }

    /// <summary>
    /// If anchor is unowned, export the anchor data stored in game object, take anchor ownership of the shared anchor,
    /// and broadcast anchor data to other players.
    /// </summary>
    public IEnumerator SetDefaultNetworkAnchorAsync(String anchorId, GameObject gameObject)
    {
        if (networkAnchorManager != null && !networkAnchorManager.IsSharedAnchorOwned)
        {
            yield return CheckOutAnchorAsync();
            if (CheckedOutAnchor && !networkAnchorManager.IsSharedAnchorOwned)
            {
                CheckInAnchor(anchorId, gameObject);
            }
        }
    }
 
    /// <summary>
    /// Check-out the shared anchor.
    /// </summary>
    /// <param name="result">Will be set to true if anchor is checked out by this player</param>
    public IEnumerator CheckOutAnchorAsync()
    {
        CheckoutRequest request;
        lock (currentRequestLock)
        {
            request = currentRequest = CheckoutRequest.CreateNext(currentRequest);
        }

        Debug.LogFormat("[NetworkAnchorPlayer] Making check-out request. {0}", DebugInfo());
        CmdCheckOutAnchor(request.Id);

        bool? checkedOut = null;
        while (!checkedOut.HasValue)
        {
            yield return null;
            lock (currentRequestLock)
            {
                if (request == currentRequest && request.Success.HasValue)
                {
                    checkedOut = request.Success.Value;
                }
                else if (request != currentRequest)
                {
                    checkedOut = false;
                }
            }
        }

        Debug.LogFormat("[NetworkAnchorPlayer] Done making check-out request. (result: {0}) {1}", CheckedOutAnchor, DebugInfo());

        if (checkedOut.Value)
        {
            lock (checkedOutAnchorCountLock)
            {
                checkedOutAnchorCount++;
            }
        }
    }

    /// <summary>
    /// MOve the given anchor id
    /// </summary>
    public void MoveAnchor(string anchorId, Vector3 positionDelta, Vector3 eulerAnglesDelta)
    {
        if (networkAnchorManager == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring move request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        Debug.LogFormat("[NetworkAnchorPlayer] Sending anchor move request. (anchorId: {0}) (positionDelta: {1}) (eulerAnglesDelta: {2}) {3}", anchorId, positionDelta, eulerAnglesDelta, DebugInfo());
        CmdMoveAnchor(anchorId, positionDelta, eulerAnglesDelta);
    }

    /// <summary>
    /// Export the anchor data stored in game object, take anchor ownership of the shared anchor, and broadcast anchor
    /// data to other players.
    /// </summary>
    public void CheckInAnchor(String anchorId, GameObject gameObject)
    {
        if (networkAnchorManager == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-in request, as there is no anchor manager. (anchor id: {0}) {1}", anchorId, DebugInfo());
            return;
        }
        
        networkAnchorManager.ExportAnchorAsync(anchorId, gameObject, ExportingAnchorComplete);
    }

    /// <summary>
    /// Invoked after the anchor data has been exported, and can now to shared to other players.
    /// </summary>
    /// <param name="sharedAnchorId">The id of the shared anchor</param>
    /// <param name="sharedGameObject">The game object that owns the anchor</param>
    /// <param name="result">The share result</param>
    private void ExportingAnchorComplete(String sharedAnchorId, GameObject sharedGameObject, NetworkAnchorManager.ExportingAnchorResult result)
    {
        // Start taking ownership of the anchor
        if (result == NetworkAnchorManager.ExportingAnchorResult.Success)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Succeeded to export. Sending anchor check-in request. {0}", DebugInfo());
            CheckInAnchor(SharedAnchorData.Create(sharedAnchorId));

            if (ExportedAnchor != null)
            {
                ExportedAnchor(this, sharedAnchorId);
            }
        }
        else
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Failed to export. Sending anchor check-in request. {0}", DebugInfo());
            CheckInAnchor(SharedAnchorData.Empty);
        }
    }

    /// <summary>
    /// Check-in a new shared anchor.
    /// </summary>
    private void CheckInAnchor(SharedAnchorData anchorData)
    {
        CmdCheckInAnchor(anchorData);

        lock (checkedOutAnchorCountLock)
        {
            if (checkedOutAnchorCount > 0)
            {
                checkedOutAnchorCount--;
            }
        }
    }

    [Command]
    private void CmdCheckOutAnchor(int requestId)
    {
        if (networkAnchorManager == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-out request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        bool checkedOut = networkAnchorManager.CheckOutAnchorSource(this);
        RpcCheckOutAnchorResult(requestId, checkedOut);
    }

    [ClientRpc]
    private void RpcCheckOutAnchorResult(int requestId, bool checkedOut)
    {
        if (hasAuthority)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Check-out completed. (checkedOut: {0}) {1}", checkedOut, DebugInfo());
            lock (currentRequestLock)
            {
                if (currentRequest.Id == requestId)
                {
                    currentRequest.Success = checkedOut;
                }
            }
        }
    }

    [Command]
    private void CmdMoveAnchor(string anchorId, Vector3 positionDelta, Vector3 eulerAnglesDelta)
    {
        if (networkAnchorManager == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring move request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        networkAnchorManager.MoveAnchorSource(this, anchorId, positionDelta, eulerAnglesDelta);
    }

    [Command]
    private void CmdCheckInAnchor(SharedAnchorData newAnchorData)
    {
        if (networkAnchorManager == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-in request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        networkAnchorManager.CheckInAnchorSource(this, newAnchorData);
    }

    /// <summary>
    /// A helper class to encapsulate a checkout request.
    /// </summary>
    private class CheckoutRequest
    {
        public CheckoutRequest(int id)
        {
            Id = id;
            Success = null;
        }

        public static CheckoutRequest CreateNext(CheckoutRequest previous)
        {
            CheckoutRequest result = new CheckoutRequest(previous.Id + 1);
            return result;
        }

        public int Id { get; private set; }
        public bool? Success { get; set; }
    }
}
