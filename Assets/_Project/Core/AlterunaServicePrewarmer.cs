using System;
using System.Reflection;
using System.Threading.Tasks;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Core.Log;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Alteruna 2.1 servis cekirdegini Unity ana thread'i disinda hazirlar.
/// SDK'nin MultiplayerManager.Start metodu Service.Start'i senkron cagiriyor;
/// bu cagri Quest/IL2CPP'de beklerse Android ilk pencere odagini alamiyor ve ANR
/// olusuyor. Hazirlik tamamlanmadan MultiplayerManager nesnesi etkinlestirilmez.
/// </summary>
[Preserve]
public static class AlterunaServicePrewarmer
{
    public sealed class Result
    {
        public bool Success { get; }
        public Exception Error { get; }

        Result(bool success, Exception error)
        {
            Success = success;
            Error = error;
        }

        public static Result Completed() => new Result(true, null);
        public static Result Failed(Exception error) => new Result(false, error);
    }

    static readonly Type ManagerType = typeof(Alteruna.Multiplayer.Unity.MultiplayerManager);
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo ServiceField = ManagerType.GetField("_service", PrivateInstance);
    static readonly FieldInfo ListenersField = ManagerType.GetField("_serviceListeners", PrivateInstance);
    static readonly FieldInfo LogField = ManagerType.GetField("_log", PrivateInstance);

    /// <remarks>
    /// Yalnizca saf Alteruna servis nesneleri worker thread'de kullanilir. Unity
    /// nesnesinden gereken tum degerler Task baslatilmadan once ana thread'de okunur.
    /// </remarks>
    public static Task<Result> Begin(Alteruna.Multiplayer.Unity.MultiplayerManager manager)
    {
        if (manager == null)
            return Task.FromResult(Result.Failed(new ArgumentNullException(nameof(manager))));

        try
        {
            if (ServiceField == null || ListenersField == null || LogField == null)
                throw new MissingFieldException(
                    "Alteruna 2.1 MultiplayerManager servis alanlari bulunamadi. Paket surumunu kontrol edin.");

            var service = ServiceField.GetValue(manager) as Service;
            var listeners = ListenersField.GetValue(manager) as IServiceListener;
            var log = LogField.GetValue(manager) as LogBase;

            if (service == null || listeners == null || log == null)
                throw new InvalidOperationException("Alteruna servis nesneleri henuz olusturulmamis.");

            if (service.Initialized)
                return Task.FromResult(Result.Completed());

            string username = BuildUsername();
            ushort maxPlayers = manager.MaxPlayers;
            var args = new Service.ServiceArgs
            {
                Username = username,
                Log = log,
                MaxPlayers = maxPlayers,
            };

            return Task.Run(() =>
            {
                try
                {
                    service.Start(listeners, args);
                    return Result.Completed();
                }
                catch (Exception exception)
                {
                    return Result.Failed(exception);
                }
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(Result.Failed(exception));
        }
    }

    static string BuildUsername()
    {
        string id = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");

        int suffixLength = Math.Min(8, id.Length);
        return "Quest-" + id.Substring(id.Length - suffixLength, suffixLength);
    }
}
