// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.WSA;
using System;
using System.Collections.Generic;

#if UNITY_WSA
using UnityEngine.XR.WSA.Sharing;
#else
/// <summary>
/// Mock enum to allow building on non-uwp platforms.
/// </summary>
public enum SerializationCompletionReason
{
    Succeeded = 0,
    NotSupported = 1,
    AccessDenied = 2,
    UnknownError = 3
}

/// <summary>
/// Mock class to allow building on non-uwp platforms.
/// </summary>
public class WorldAnchorTransferBatch
{
    public delegate void SerializationDataAvailableDelegate(byte[] data);
    public delegate void SerializationCompleteDelegate(SerializationCompletionReason completionReason);
    public delegate void DeserializationCompleteDelegate(SerializationCompletionReason completionReason, WorldAnchorTransferBatch deserializedTransferBatch);

    public int anchorCount
    {
        get
        {
            return 0;
        }
    }

    public string[] GetAllIds()
    {
        return new string[0];
    }

    public void AddWorldAnchor(string id, WorldAnchor anchor)
    {
    }

    public WorldAnchor LockObject(string id, GameObject obj)
    {
        return null;
    }

    public static void ExportAsync(
        WorldAnchorTransferBatch batch, 
        SerializationDataAvailableDelegate dataBuffer,
        SerializationCompleteDelegate completeCallback)
    {
        if (completeCallback != null)
        {
            completeCallback(SerializationCompletionReason.NotSupported);
        }
    }

    public static void ImportAsync(
        byte[] data,
        DeserializationCompleteDelegate completeCallback)
    {
        if (completeCallback != null)
        {
            completeCallback(SerializationCompletionReason.NotSupported, null);
        }
    }
}
#endif

/// <summary>
/// Data describing the source of the shared anchor data.
/// </summary>
[Serializable]
public struct SharedAnchorData
{
    /// <summary>
    /// Create a new shared anchor data object.
    /// </summary>
    /// <param name="anchorId">The id of the anchor to share.</param>
    public static SharedAnchorData Create(string anchorId)
    {
        SharedAnchorData result = new SharedAnchorData();
        result.SourceIp = NetworkAnchorManager.Instance == null ? "unknown" : NetworkAnchorManager.Instance.LocalAddress;
        result.AnchorId = anchorId;
        return result;
    }

    /// <summary>
    /// Get an empty, invalid, anchor data
    /// </summary>
    public static SharedAnchorData Empty
    {
        get
        {
            return new SharedAnchorData();
        }
    }

    /// <summary>
    /// The IP address of the player that is serving the most recent anchor
    /// </summary>
    public string SourceIp;

    /// <summary>
    /// The id (or name) of the anchor.
    /// </summary>
    public string AnchorId;
    /// <summary>
    /// Test if this meta data object hold valid information
    /// </summary>
    public bool IsValid
    {
        get
        {
            return !String.IsNullOrEmpty(SourceIp) && !String.IsNullOrEmpty(AnchorId);
        }
    }

    public override string ToString()
    {
        return String.Format("(anchor source ip: {0}) (anchor id: {1})", SourceIp, AnchorId);
    }
}

public class ImportedAnchorChangedArgs
{
    public ImportedAnchorChangedArgs(WorldAnchorTransferBatch transferBatch)
    {
        TransferBatch = transferBatch;
    }

    /// <summary>
    /// The transfer batch containing the anchor
    /// </summary>
    public WorldAnchorTransferBatch TransferBatch { get; private set; }

    /// <summary>
    /// The first anchor id in the transfer batch
    /// </summary>
    public string AnchorId
    {
        get
        {
            if (TransferBatch == null)
            {
                return String.Empty;
            }

            var ids = TransferBatch.GetAllIds();
            if (ids == null || ids.Length == 0)
            {
                return String.Empty;
            }

            return ids[0];
        }
    }
}

/// <summary>
/// A struct that contains predicted movements of a network anchor
/// </summary>
public struct NetworkAnchorMovement
{
    public static NetworkAnchorMovement Create(string anchorId)
    {
        NetworkAnchorMovement movement = new NetworkAnchorMovement();
        movement.AnchorId = anchorId;
        movement.Position = Vector3.zero;
        movement.EulerAngles = Vector3.zero;
        return movement;
    }

    public static NetworkAnchorMovement Create(string anchorId, Vector3 position, Vector3 eulerAngles)
    {
        NetworkAnchorMovement movement = new NetworkAnchorMovement();
        movement.AnchorId = anchorId;
        movement.Position = position;
        movement.EulerAngles = eulerAngles;
        return movement;
    }

    public string AnchorId;
    public Vector3 Position;
    public Vector3 EulerAngles;
}

/// <summary>
/// A sync list that shares anchor movements across clients
/// </summary>
public class NetworkAnchorMovementList : SyncListStruct<NetworkAnchorMovement>
{
}


public class NetworkAnchorManager : NetworkBehaviour
{
    /// <summary>
    /// Get the local instance of the anchor manager
    /// </summary>
    private static NetworkAnchorManager instance;
    public static NetworkAnchorManager Instance
    {
        get
        {
            return instance;
        }
    }

    /// <summary>
    /// The sync var used to determine what client own the anchor. Private to prevent outside componets from changing this.
    /// </summary>
    [SyncVar(hook = "SyncVar_AnchorSourceChanged")]
    [HideInInspector]
    private SharedAnchorData SyncVar_AnchorSource;

    /// <summary>
    /// The sync var used to notify other clients the anchor source is checked out
    /// </summary>
    [HideInInspector]
    private bool SyncVar_AnchorSourceCheckedOut;

    /// <summary>
    /// The sync list used to share anchor movements across clients.
    /// </summary>
    [HideInInspector]
    private NetworkAnchorMovementList SyncList_AnchorMovements = new NetworkAnchorMovementList();

    /// <summary>
    /// The player that currently has the AnchorSource checked out. Only one player can edit the anchor at a time
    /// </summary>
    private NetworkAnchorPlayer AnchorSourceCheckedOutBy;

    /// <summary>
    /// A lock to protect AnchorSourceCheckedOutBy
    /// </summary>
    private object AnchorCheckoutLock = new object();

    /// <summary>
    /// This event is raised when a new anchor arrives from a different player.
    /// </summary>
    /// <param name="args">Contains the data that arrived.</param>
    public delegate void OnImportedAnchorChanged(NetworkAnchorManager sender, ImportedAnchorChangedArgs args);
    public event OnImportedAnchorChanged ImportedAnchorChanged;

    /// <summary>
    /// Get if the local anchor player is in the process of receiving a shared anchor.
    /// </summary>
    public bool ImportingAnchor
    {
        get
        {
            return ImportingAnchorSource.IsValid || SyncVar_AnchorSourceCheckedOut;
        }
    }

    /// <summary>
    /// The current anchor that's loading
    /// </summary>
    public SharedAnchorData ImportingAnchorSource { get; private set; }

    /// <summary>
    /// The current anchor that's being exported
    /// </summary>
    public SharedAnchorData ExportingAnchorSource { get; private set; }

    /// <summary>
    /// Get the last received remote anchor
    /// </summary>
    public ImportedAnchorChangedArgs ImportedAnchor { get; private set; }

    /// <summary>
    /// An object to lock when accessing LoadingAnchorSource
    /// </summary>
    private object ImportingAndExportingLock = new object();

    /// <summary>
    /// Get the local IP address.
    /// </summary>
    public String LocalAddress { get; private set; }

    /// <summary>
    /// Get if the anchor is currently owned by a player.
    /// </summary>
    public bool IsSharedAnchorOwned
    {
        get
        {
            return SyncVar_AnchorSource.IsValid;
        }
    }

    /// <summary>
    /// Get if this is the anchor source owner
    /// </summary>
    public bool IsAnchorSourceOwner
    {
        get
        {
            return IsSharedAnchorOwned && SyncVar_AnchorSource.SourceIp == LocalAddress;
        }
    }

    /// <summary>
    /// This will send or receive the binary anchor data on the local instance
    /// </summary>
    private GenericNetworkTransmitter anchorTransmitter;

    /// <summary>
    /// Retry failed exports every second.
    /// </summary>
    private const float retryDelaySeconds = 1.0f;

    /// <summary>
    /// The number of times to retry exporting before giving up
    /// </summary>
    private const int retryExportAttempts = 30;

    #region AnchorSource Operations
    /// <summary>
    /// Check out the anchor source
    /// </summary>
    [Server]
    public bool CheckOutAnchorSource(NetworkAnchorPlayer player)
    {
        if (player == null)
        {
            return false;
        }

        bool checkedOut = false;
        lock (AnchorCheckoutLock)
        {
            if (AnchorSourceCheckedOutBy == null)
            {
                Debug.LogFormat("[NetworkAnchorManager] Server checked out anchor source. (player.netId: {0})", player.netId);
                checkedOut = true;
                SyncVar_AnchorSourceCheckedOut = true;
                AnchorSourceCheckedOutBy = player;
            }
            else
            {
                Debug.LogFormat("[NetworkAnchorManager] Server could not checked out anchor source, already checked out. (player.netId: {0}) {1}", player.netId, DebugInfo());
            }
        }

        return checkedOut;
    }

    /// <summary>
    /// Move the anchor source
    /// </summary>
    [Server]
    public bool MoveAnchorSource(NetworkAnchorPlayer player, string anchorId, Vector3 positionDelta, Vector3 eulerAnglesDelta)
    {
        bool moved = false;
        lock (AnchorCheckoutLock)
        {
            if (AnchorSourceCheckedOutBy != null && player.netId == AnchorSourceCheckedOutBy.netId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Server moved anchor source. (player.netId: {0}) (anchorId: {1}) (positionDelta: {2}) (eulerAnglesDelta: {3}) {4}", player.netId, anchorId, positionDelta, eulerAnglesDelta, DebugInfo());
                MoveAllAnchors(positionDelta, eulerAnglesDelta);
                moved = true;
            }
            else
            {
                Debug.LogFormat("[NetworkAnchorManager] Server could not move anchor source. (player.netId: {0}) (anchorId: {1}) (positionDelta: {2}) (eulerAnglesDelta: {3}) {4}", player.netId, anchorId, positionDelta, eulerAnglesDelta, DebugInfo());
            }
        }
        return moved;
    }

    /// <summary>
    /// Check in anchor source
    /// </summary>
    [Server]
    public bool CheckInAnchorSource(NetworkAnchorPlayer player, SharedAnchorData anchorData)
    {
        bool checkedIn = false;
        lock (AnchorCheckoutLock)
        {
            if (AnchorSourceCheckedOutBy != null && player.netId == AnchorSourceCheckedOutBy.netId)
            {
                checkedIn = true;
                AnchorSourceCheckedOutBy = null;

                if (anchorData.IsValid)
                {
                    SyncVar_AnchorSource = anchorData;
                }

                SyncVar_AnchorSourceCheckedOut = false;
                Debug.LogFormat("[NetworkAnchorManager] Server checked in anchor source. (player.netId: {0}) {1} {2} {3}", player.netId, anchorData.ToString(), SyncVar_AnchorSource.ToString(), DebugInfo());
            }
            else
            {
                Debug.LogFormat("[NetworkAnchorManager] Server could not check in anchor source. (player.netId: {0}) {1} {2} {3}", player.netId, anchorData.ToString(), SyncVar_AnchorSource.ToString(), DebugInfo());
            }
        }
        return checkedIn;
    }
    #endregion AnchorSource Operations

    #region Exporting Anchor Methods
    /// <summary>
    /// The results of sharing an anchor
    /// </summary>
    public enum ExportingAnchorResult
    {
        Unknown,
        Success,
        FailedDisplayIsOpaque,
        FailedAnchorIsAlreadyShared,
        FailedAnchorWasJustReceived,
        FailedGameObjectMissingAnchor,
        FailedTimedOut,
        FailedRetriedTooManyTimes,
        FailedInvalidAnchorId,
        FailedUnknown
    }

    /// <summary>
    /// Delegate invoked when sharing attempt has completed.
    /// </summary>
    /// <param name="anchorId">
    /// The anchor id being shared
    /// </param>
    /// <param name="gameObject">
    /// The game object that owns the anchor
    /// </param>
    /// <param name="success">
    /// True if sharing was successful. False otherwise
    /// </param>
    public delegate void ExportingAnchorCompleteDelegate(String anchorId, GameObject gameObject, ExportingAnchorResult result);

    /// <summary>
    /// Try exporting the anchor data stored in game object, and broadcast anchor data with other players.
    /// </summary>
    /// <summary>
    /// Return true if exporting was able start
    /// </summary>
    [Client]
    public bool ExportAnchorAsync(String anchorId, GameObject gameObject, ExportingAnchorCompleteDelegate completeDelegate)
    {
        return ExportAnchorAsync(1, anchorId, gameObject, completeDelegate);
    }

    public bool ExportAnchorAsync(int attempts, String anchorId, GameObject gameObject, ExportingAnchorCompleteDelegate completeDelegate)
    {
        ExportingAnchorResult result = ExportingAnchorResult.Unknown;
        lock (ImportingAndExportingLock)
        {
            WorldAnchor worldAnchor = gameObject.GetComponent<WorldAnchor>();

            if (HolographicSettings.IsDisplayOpaque)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring export anchor request, as this device doesn't support anchoring. (anchor id: {0})", anchorId);
                result = ExportingAnchorResult.FailedDisplayIsOpaque;
            }
            else if (SyncVar_AnchorSource.AnchorId == anchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring export anchor request, as anchor is already being shared. (anchor id: {0})", anchorId);
                result = ExportingAnchorResult.FailedAnchorIsAlreadyShared;
            }
            else if (ImportedAnchor != null && ImportedAnchor.AnchorId == anchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring export anchor request, as anchor was just received. (anchor id: {0})", anchorId);
                result = ExportingAnchorResult.FailedAnchorWasJustReceived;
            }
            else if (worldAnchor == null)
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Unable to export anchor. Game object is missing an anchor. (anchor id: {0})", anchorId);
                result = ExportingAnchorResult.FailedGameObjectMissingAnchor;
            }
            else if (attempts > retryExportAttempts)
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Failed to export, attempted to retry exporting too many times. (anchor id: {0})", anchorId);
                result = ExportingAnchorResult.FailedRetriedTooManyTimes;
            }

            if (result == ExportingAnchorResult.Unknown)
            {
                Debug.LogFormat("[NetworkAnchorManager] Attempting to export an anchor, so it can be shared with other players. (new anchor id: {0}) {1} {2}", anchorId, SyncVar_AnchorSource.ToString(), DebugInfo());

                try
                {
                    // Stop all pending work on the anchor transmitter
                    anchorTransmitter.StopAll();

                    // Export binary data
                    List<byte> buffer = new List<byte>();
                    WorldAnchorTransferBatch batch = new WorldAnchorTransferBatch();
                    batch.AddWorldAnchor(anchorId, worldAnchor);
                    WorldAnchorTransferBatch.ExportAsync(
                        batch,
                        (byte[] data) => { buffer.AddRange(data); },
                        (SerializationCompletionReason status) => { ExportAnchorDataComplete(attempts, status, buffer.ToArray(), anchorId, gameObject, completeDelegate); });
                }
                catch (Exception e)
                {
                    Debug.LogFormat("[NetworkAnchorManager] Unknown error occurred when trying to export anchor. (exception message: {0}) (new anchor id: {1}) {2} {3}", e.Message, anchorId, SyncVar_AnchorSource.ToString(), DebugInfo());
                    result = ExportingAnchorResult.FailedUnknown;
                }
            }

            if (result == ExportingAnchorResult.Unknown)
            {
                // The last received anchor will no longer be relevant since we're taking ownership
                ImportedAnchor = null;

                // no longer loading an anchor
                ImportingAnchorSource = SharedAnchorData.Empty;

                // save the anchor being exported
                ExportingAnchorSource = SharedAnchorData.Create(anchorId);
            }
        }

        // Notify callback of failure
        if (result != ExportingAnchorResult.Unknown && completeDelegate != null)
        {
            completeDelegate(anchorId, gameObject, result);
        }

        return result == ExportingAnchorResult.Unknown;
    }

    /// <summary>
    /// This is invoked once we've finished exported the binary anchor data to a byte array.
    /// </summary>
    private void ExportAnchorDataComplete(
        int attempts, 
        SerializationCompletionReason status,
        byte[] data,
        String anchorId,
        GameObject gameObject,
        ExportingAnchorCompleteDelegate completeDelegate)
    {
        ExportingAnchorResult result = ExportingAnchorResult.Unknown;
        lock (ImportingAndExportingLock)
        {
            if (ImportingAnchorSource.IsValid || ImportedAnchor != null)
            {
                Debug.LogFormat("[NetworkAnchorManager] Exporting anchor completed, but local client is now using a remote anchor. (anchor id: {0}) (bytes: {1}) (export result: {2}) {3}", anchorId, data.Length, status.ToString(), DebugInfo());
                result = ExportingAnchorResult.FailedTimedOut;
            }
            else if (ExportingAnchorSource.AnchorId != anchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Exporting anchor completed, but exporting anchor id changed. (anchor id: {0}) (bytes: {1}) {2}", anchorId, data.Length, DebugInfo());
                result = ExportingAnchorResult.FailedInvalidAnchorId;
            }
            else if (status == SerializationCompletionReason.Succeeded)
            {
                Debug.LogFormat("[NetworkAnchorManager] Exporting anchor succeeded. (anchor id: {0}) (bytes: {1}) {2}", anchorId, data.Length, DebugInfo());
                anchorTransmitter.SendData(data);
                result = ExportingAnchorResult.Success;
                ExportingAnchorSource = SharedAnchorData.Empty;
            }
            else
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Exporting anchor failed, going to retry. (anchor id: {0}) (status: {1}) (bytes: {2}) {3}", anchorId, status, data.Length, DebugInfo());
                StartCoroutine(RetrySharingAnchor(attempts, anchorId, gameObject, completeDelegate));
            }
        }

        if (result != ExportingAnchorResult.Unknown && completeDelegate != null)
        {
            completeDelegate(anchorId, gameObject, result);
        }
    }

    /// <summary>
    /// Retry sharing anchor, if it's still possible to.
    /// </summary>
    private System.Collections.IEnumerator RetrySharingAnchor(int attempts, String anchorId, GameObject gameObject, ExportingAnchorCompleteDelegate completeDelegate)
    {
        yield return new WaitForSeconds(retryDelaySeconds);

        bool retry = false;
        lock (ImportingAndExportingLock)
        {
            // If loading and received an anchor, don't continue to try to share anchor data.
            if (ExportingAnchorSource.AnchorId == anchorId && !ImportingAnchorSource.IsValid && ImportedAnchor == null)
            {
                retry = true;
            }
        }

        if (retry)
        {
            ExportAnchorAsync(attempts + 1, anchorId, gameObject, completeDelegate);
        }
        else if (completeDelegate != null)
        {
            Debug.LogFormat("[NetworkAnchorManager] Can't retry sharing anchor, local client is now using remote anchor or is exporting a different anchor. (anchor id: {0}) {1}", anchorId, DebugInfo());
            completeDelegate(anchorId, gameObject, ExportingAnchorResult.FailedTimedOut);
        }
    }
    #endregion Exporting Anchor Methods

    #region Importing Anchor Methods
    /// <summary>
    /// When client starts, check if an anchor needs to be imported
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        ImportAnchorData(SyncVar_AnchorSource);
    }

    /// <summary>
    /// Begin import anchor data from source.
    /// </summary>
    private void SyncVar_AnchorSourceChanged(SharedAnchorData anchorSource)
    {
        InitializeAnchorMovement(anchorSource.AnchorId);
        ImportAnchorData(anchorSource);
    }

    /// <summary>
    /// Begin import anchor data from source.
    /// </summary>
    private bool ImportAnchorData(SharedAnchorData anchorSource)
    {
        lock (ImportingAndExportingLock)
        {
            if (HolographicSettings.IsDisplayOpaque)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring import anchor request, as this device doesn't support anchoring. {0} {1}", anchorSource.ToString(), DebugInfo());
                return false;
            }

            if (!anchorSource.IsValid)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring import anchor request, since the anchor is invalid. {0} {1}", anchorSource.ToString(), DebugInfo());
                return false;
            }

            if (anchorSource.SourceIp == LocalAddress)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring import anchor request, since it originated from this player. {0} {1}", anchorSource.ToString(), DebugInfo());
                return false;
            }

            if (ImportedAnchor != null && anchorSource.AnchorId == ImportedAnchor.AnchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] IIgnoring import anchor request, since it's already imported. {0} {1}", anchorSource.ToString(), DebugInfo());
                return false;
            }

            if (ImportingAnchorSource.IsValid && ImportingAnchorSource.AnchorId == anchorSource.AnchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Ignoring import anchor request, since it's already being imported. {0} {1}", anchorSource.ToString(), DebugInfo());
                return false;
            }

            Debug.LogFormat("[NetworkAnchorManager] Importing anchor. {0} {1}", anchorSource.ToString(), DebugInfo());

            // no longer exported an anchor
            ExportingAnchorSource = SharedAnchorData.Empty;

            // save anchor being imported
            ImportingAnchorSource = anchorSource;

            // begin requesting data
            anchorTransmitter.RequestData(ImportingAnchorSource.AnchorId, ImportingAnchorSource.SourceIp);

            return true;
        }
    }

    /// <summary>
    /// Called to begin the process of importing pending anchor data
    /// </summary>
    private void RequestAnchorDataCompleted(GenericNetworkTransmitter sender, RequestDataCompletedArgs args)
    {
        lock (ImportingAndExportingLock)
        {
            if (!args.Successful)
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Failed to receive anchor data. {0}", DebugInfo());
                ImportAnchorDataCompleted(null);
                return;
            }

            if (args.Data == null || args.Data.Length == 0)
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Binary anchor data is null or empty, ignoring request to import anchor data. {0}", DebugInfo());
                ImportAnchorDataCompleted(null);
                return;
            }

            if (args.RequestId != ImportingAnchorSource.AnchorId)
            {
                Debug.LogErrorFormat("[NetworkAnchorManager] Received data for anchor, but no longer importing this anchor id. (received anchor: {0}), {1}", DebugInfo());
                ImportAnchorDataCompleted(null);
                return;
            }
        }

        StartImportingAnchor(args.RequestId, args.Data);
    }

    /// <summary>
    /// Start importing anchor data.
    /// </summary>
    private void StartImportingAnchor(string anchorId, byte[] anchorData)
    {
        Debug.LogFormat("[NetworkAnchorManager] Starting import of binary anchor data. (bytes: {0}) {1}", anchorData.Length, DebugInfo());
        WorldAnchorTransferBatch.ImportAsync(anchorData, (SerializationCompletionReason status, WorldAnchorTransferBatch batch) => { BatchImportAsyncCompleted(anchorId, status, batch, anchorData); });
    }

    /// <summary>
    /// Called when a remote anchor has been de-serialized
    /// </summary>
    /// <param name="status">Tracks if the import worked</param>
    /// <param name="batch">The WorldAnchorTransferBatch that has the anchor information.</param>
    private void BatchImportAsyncCompleted(
        string anchorId,
        SerializationCompletionReason status,
        WorldAnchorTransferBatch batch,
        byte[] anchorData)
    {
        if (status == SerializationCompletionReason.Succeeded && batch.anchorCount > 0)
        {
            Debug.Log("[NetworkAnchorManager] Anchor import was successful.");
            ImportAnchorDataCompleted(batch);
        }
        else
        {
            Debug.LogErrorFormat("[NetworkAnchorManager] Anchor import has failed, retrying (status: {0}) (batch.anchorCount: {1})", status, batch.anchorCount);
            StartCoroutine(RetryImportingAnchor(anchorId, anchorData));
        }
    }

    /// <summary>
    /// Retry sharing anchor, if it's still possible to.
    /// </summary>
    private System.Collections.IEnumerator RetryImportingAnchor(string anchorId, byte[] data)
    {
        yield return new WaitForSeconds(retryDelaySeconds);

        lock (ImportingAndExportingLock)
        {
            // If loading and received an anchor, don't continue to try to share anchor data.
            if (ImportingAnchorSource.IsValid && anchorId == ImportingAnchorSource.AnchorId)
            {
                StartImportingAnchor(anchorId, data);
            }
            else
            {
                Debug.LogFormat("[NetworkAnchorManager] Can't retry importing anchor, not using this remote anchor anymore. (old anchor id: {0}) {1}", anchorId, DebugInfo());
            }
        }
    }

    /// <summary>
    /// The final function called once a network anchor has been imported.
    /// </summary>
    private void ImportAnchorDataCompleted(WorldAnchorTransferBatch batch)
    {
        lock (ImportingAndExportingLock)
        {
            // Validate batch, and if an imported anchor is needed
            if (!ImportingAnchorSource.IsValid)
            {
                Debug.LogFormat("[NetworkAnchorManager] Imported anchor, but we're no longer wanting to import an anchor {0}", DebugInfo());
                return;
            }

            if (batch == null || batch.anchorCount == 0)
            {
                Debug.LogFormat("[NetworkAnchorManager] Imported anchor, but ignoring since batch was empty. {0}", DebugInfo());
            }

            // Make a new set of change args
            var newImportedAnchor = new ImportedAnchorChangedArgs(batch);

            // Make sure we've received the anchor we were expecting to load
            if (newImportedAnchor.AnchorId != ImportingAnchorSource.AnchorId)
            {
                Debug.LogFormat("[NetworkAnchorManager] Imported anchor, but ignoring since this anchor is no longer being imported. {0}", DebugInfo());
                return;
            }

            // save the last recieve anchor data
            ImportedAnchor = newImportedAnchor;

            // no longer loading an anchor
            ImportingAnchorSource = SharedAnchorData.Empty;

            if (ImportedAnchorChanged != null)
            {
                ImportedAnchorChanged(this, ImportedAnchor);
            }
        }
    }
    #endregion Importing Anchor Methods

    #region Anchor Movement Methods
    /// <summary>
    /// Insert a new anchor into the movement list, if that anchor doesn't exist
    /// </summary>
    /// <param name="anchorId">The id of the new anchor</param>
    [Server]
    private void InitializeAnchorMovement(string anchorId)
    {
        NetworkAnchorMovement? movement = FindNetworkAnchorMovement(anchorId);
        if (!movement.HasValue)
        {
            Debug.LogFormat("[NetworkAnchorManager] Server is adding new anchor movement to list. (anchorId: {0}) {1}", anchorId, DebugInfo());
            SyncList_AnchorMovements.Add(NetworkAnchorMovement.Create(anchorId));
        }
    }

    /// <summary>
    /// Move all the known anchors.
    /// </summary>
    [Server]
    private void MoveAllAnchors(Vector3 positionDelta, Vector3 eulerAnglesDelta)
    {
        Debug.LogFormat("[NetworkAnchorManager] Server is moving all anchors currently in movement list. (positionDelta: {0}) (eulerAnglesDelta: {1}) (SyncList_AnchorMovements.Count: {2}) {3}", positionDelta, eulerAnglesDelta, SyncList_AnchorMovements.Count, DebugInfo());
        for (int i = 0; i < SyncList_AnchorMovements.Count; i++)
        {
            var oldMovement = SyncList_AnchorMovements[i];
            SyncList_AnchorMovements[i] = NetworkAnchorMovement.Create(
                oldMovement.AnchorId, 
                oldMovement.Position + positionDelta, 
                oldMovement.EulerAngles + eulerAnglesDelta);
        }
    }

    /// <summary>
    /// Search for the given anchorId in the SyncList_AnchorMovements. If not found, null is returned.
    /// </summary>
    private NetworkAnchorMovement? FindNetworkAnchorMovement(string anchorId)
    {
        NetworkAnchorMovement? result = null;

        // search backwards, as the item is more like to be at the end
        for (int i = SyncList_AnchorMovements.Count - 1; i >= 0; i--)
        {
            var item = SyncList_AnchorMovements[i];
            if (item.AnchorId == anchorId)
            {
                result = item;
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Apply the anchor movements to the given transform
    /// </summary>
    public void ApplyMovement(string anchorId, GameObject target)
    {
        if (target == null)
        {
            return;
        }

        NetworkAnchorMovement? movement = FindNetworkAnchorMovement(anchorId);

        if (movement.HasValue)
        {
            var value = movement.Value;
            target.transform.localPosition = value.Position;
            target.transform.localRotation = Quaternion.Euler(value.EulerAngles);
        }
        else
        {
            target.transform.localPosition = Vector3.zero;
            target.transform.localRotation = Quaternion.identity;
        }
    }
    #endregion Anchor Movement Methods

    #region Initialization Methods
    /// <summary>
    /// On creation save this as the static instance. There should be only one manager.
    /// </summary>
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        instance = this;

        this.InitializeLocalAddress();
        this.InitializeAnchorTransmitterOnce();
    }

    /// <summary>
    /// Initialize the local IP address. This should only be called with authority.
    /// </summary>
    private void InitializeLocalAddress()
    {
#if !UNITY_EDITOR
        foreach (Windows.Networking.HostName hostName in Windows.Networking.Connectivity.NetworkInformation.GetHostNames())
        {
            if (hostName.DisplayName.Split(".".ToCharArray()).Length == 4)
            {
                LocalAddress = hostName.DisplayName;
                Debug.LogFormat("[NetworkAnchorManager] Found local ip address. {0}", DebugInfo());
                break;
            }
        }

        if (string.IsNullOrEmpty(LocalAddress))
        {
            Debug.LogErrorFormat("[NetworkAnchorManager] Failed to find local ip address. {0}", DebugInfo());
        }
#else
        LocalAddress = "editor" + UnityEngine.Random.Range(0, 999999).ToString();
#endif
    }

    /// <summary>
    /// Initialize the anchor transmitter only once
    /// </summary>
    private void InitializeAnchorTransmitterOnce()
    {
        if (anchorTransmitter == null)
        {
            anchorTransmitter = new GenericNetworkTransmitter();
            anchorTransmitter.RequestDataCompleted += RequestAnchorDataCompleted;
        }
    }
    #endregion Initialization Methods

    #region Debug Methods
    private string DebugInfo()
    {
        return string.Format("(netId: {0}) (isLocalPlayer: {1}) (isServer: {2}) (isClient: {3}) (hasAuthority: {4}) (local ip: {5})",
            netId,
            isLocalPlayer,
            isServer,
            isClient,
            hasAuthority,
            LocalAddress);
    }
    #endregion Debug Methods
}
