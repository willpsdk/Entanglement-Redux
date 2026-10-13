using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

using Entanglement.Extensions;

using MelonLoader;

namespace Entanglement.Network
{
    public enum NetworkChannel : byte {
        Reliable    = 0,
        Unreliable  = 1,
        Attack      = 2,
        Object      = 3,
        Transaction = 4
    }

    public class NetworkMessage {
        public byte messageType;
        public byte[] messageData = new byte[0];

        public byte[] GetBytes()
        {
            byte[] bytes = new byte[1 + messageData.Length];

            bytes[0] = messageType;

            // Every message sent or relayed goes through here.
            Buffer.BlockCopy(messageData, 0, bytes, 1, messageData.Length);

            return bytes;
        }



        // Handlers are created up front, they're not static
        public static void RegisterHandlersFromAssembly(Assembly targetAssembly) {
            if (targetAssembly == null) throw new NullReferenceException("Can't register from a null assembly!");

            EntangleLogger.Log($"Populating MessageHandler list from {targetAssembly.GetName().Name}!");

            // I am aware LINQ is kinda gross but this is works!
            targetAssembly.GetTypes()
                .Where(type => typeof(NetworkMessageHandler).IsAssignableFrom(type) && !type.IsAbstract)
                .Where(type => type.GetCustomAttribute<Net.NoAutoRegister>() == null)
                .ForEach(type => {
                    try
                    {
                        RegisterHandler(type);
                    }
                    catch (Exception e)
                    {
                        EntangleLogger.Error(e.Message);
                    }
                });
        }

        public static void RegisterHandler<T>() where T : NetworkMessageHandler => RegisterHandler(typeof(T));

        protected static void RegisterHandler(Type type)
        {
            NetworkMessageHandler handler = Activator.CreateInstance(type) as NetworkMessageHandler;

            if (handler.MessageIndex == null)
            {
                EntangleLogger.Warn($"Didn't register {type.Name} because its message index was null!");
            }
            else
            {
                byte index = handler.MessageIndex.Value;

                if (handlers[index] != null) throw new Exception($"{type.Name} has the same index as {handlers[index].GetType().Name}, we can't replace handlers!");

                EntangleLogger.Log($"Registered {type.Name}");

                // We give the handler access to all known attributes so we can put them into effect
                var attributes = type.GetCustomAttributes();
                List<Type> types = new List<Type>();
                foreach (Attribute attribute in attributes) {
                    if (attribute is Net.NoAutoRegister)
                        continue;
                    types.Add(attribute.GetType());
                }
                handler.Attributes = types.ToArray();

                handlers[index] = handler;
            }
        } 

        public static NetworkMessage CreateMessage(byte type, NetworkMessageData data) {
            try {
                NetworkMessage message = handlers[type].CreateMessage(data);
                if (message != null)
                    NetStats.CountBuilt(type, message.messageData != null ? message.messageData.Length : 0);
                return message;
            }
            catch (Exception e) {
                EntangleLogger.Error($"Failed creating network message with reason: {e.Message}\nTrace:{e.StackTrace}");
            }

            return null;
        }

        public static void ReadMessage(NetworkMessage message, long sender) {
            try {
                NetStats.CountRead(message.messageType);
                handlers[message.messageType].ReadMessage(message, sender);
            }
            catch (Exception e) {
                ReportReadFailure(message.messageType, e);
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, int> readFailures = new System.Collections.Generic.Dictionary<string, int>();

        static void ReportReadFailure(byte type, Exception e) {
            string key = type + ":" + e.GetType().Name;
            int count;
            readFailures.TryGetValue(key, out count);
            count++;
            readFailures[key] = count;

            if (count <= 3)
                EntangleLogger.Error($"Failed handling network message {type} with reason: {e.Message}\nTrace:{e.StackTrace}");
            else if (count % 200 == 0)
                EntangleLogger.Error($"Failed handling network message {type} {count} times now: {e.Message}");
        }

        public static readonly NetworkMessageHandler[] handlers = new NetworkMessageHandler[byte.MaxValue];
    }
}
