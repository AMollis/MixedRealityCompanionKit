// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

public class NetworkAnchorPlayer : NetworkBehaviour
{
    /// <summary>
    /// The local anchor player.
    /// </summary>
    private static NetworkAnchorPlayer localInstance;

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
    public static NetworkAnchorPlayer LocalInstance
    {
        get
        {
            return localInstance;
        }
    }

    /// <summary>
    /// Get if this player currently has the anchor checked out.
    /// </summary>
    public bool CheckedOutAnchor
    {
        get;
        private set;
    }


    /// <summary>
    /// Prevent client from being destroyed on scene changes.
    /// </summary>
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
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
            localInstance = this;
        }
    }

    /// <summary>
    /// If anchor is unowned, export the anchor data stored in game object, take anchor ownership of the shared anchor,
    /// and broadcast anchor data to other players.
    /// </summary>
    public IEnumerator SetDefaultNetworkAnchorAsync(String anchorId, GameObject gameObject)
    {
        if (NetworkAnchorManager.Instance != null && !NetworkAnchorManager.Instance.IsSharedAnchorOwned)
        {
            yield return CheckoutAnchorAsync();
            if (CheckedOutAnchor && !NetworkAnchorManager.Instance.IsSharedAnchorOwned)
            {
                CheckinAnchor(anchorId, gameObject);
            }
        }
    }
 
    /// <summary>
    /// Check-out the shared anchor.
    /// </summary>
    /// <param name="result">Will be set to true if anchor is checked out by this player</param>
    public IEnumerator CheckoutAnchorAsync()
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
        CheckedOutAnchor = checkedOut.Value;
    }

    /// <summary>
    /// Send a move request to the anchor manager server.
    /// </summary>
    public void MoveAnchor(Vector3 offset)
    {
        if (NetworkAnchorManager.Instance == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring move request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        Debug.LogFormat("[NetworkAnchorPlayer] Sending anchor move request. (offset: {0}) {1}", offset, DebugInfo());
        CmdMoveAnchor(NetworkAnchorManager.Instance.AnchorSource, offset);
    }

    /// <summary>
    /// Export the anchor data stored in game object, take anchor ownership of the shared anchor, and broadcast anchor
    /// data to other players.
    /// </summary>
    public void CheckinAnchor(String anchorId, GameObject gameObject)
    {
        if (NetworkAnchorManager.Instance == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-in request, as there is no anchor manager. (anchor id: {0}) {1}", anchorId, DebugInfo());
            return;
        }
        
        NetworkAnchorManager.Instance.ExportAnchorAsync(anchorId, gameObject, ExportingAnchorComplete);
    }

    /// <summary>
    /// Invoked after the anchor data has been exported, and can now to shared to other players.
    /// </summary>
    /// <param name="sharedAnchorId">The id of the shared anchor</param>
    /// <param name="sharedGameObject">The game object that owns the anchor</param>
    /// <param name="result">The share result</param>
    private void ExportingAnchorComplete(String sharedAnchorId, GameObject sharedGameObject, NetworkAnchorManager.SharingAnchorResult result)
    {
        // Start taking ownership of the anchor
        if (result == NetworkAnchorManager.SharingAnchorResult.Success)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Succeeded to export. Sending anchor check-in request. {0}", DebugInfo());
            CmdCheckinAnchor(SharedAnchorData.Create(sharedAnchorId));
        }
        else
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Failed to export. Sending anchor check-in request. {0}", DebugInfo());
            CmdCheckinAnchor(SharedAnchorData.Empty);
        }

        CheckedOutAnchor = false;
    }

    [Command]
    private void CmdCheckOutAnchor(int requestId)
    {
        if (NetworkAnchorManager.Instance == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-out request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        bool checkedOut = NetworkAnchorManager.Instance.CheckoutAnchorSource(this);
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
    private void CmdMoveAnchor(SharedAnchorData anchorData, Vector3 offset)
    {
        if (NetworkAnchorManager.Instance == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring move request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        NetworkAnchorManager.Instance.MoveAnchorSource(this, anchorData, offset);
    }

    [Command]
    private void CmdCheckinAnchor(SharedAnchorData newAnchorData)
    {
        if (NetworkAnchorManager.Instance == null)
        {
            Debug.LogFormat("[NetworkAnchorPlayer] Ignoring check-in request, as there is no anchor manager. {0}", DebugInfo());
            return;
        }

        NetworkAnchorManager.Instance.CheckinAnchorSource(this, newAnchorData);
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
