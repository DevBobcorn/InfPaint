using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace MaskCreator.Utils
{
    public static class SimpleJsonSerializer
    {
        /// <summary>
        /// Implement this interface if you want custom
        /// json serialization for your class
        /// </summary>
        public interface IJSONSerializable
        {
            public string ToJson();
        }

        private static string Dictionary2Json(Dictionary<string, object> dictionary)
        {
            return "{" + string.Join(",", dictionary.Select(x => $"\"{x.Key}\":{Object2Json(x.Value)}")) + "}";
        }

        private static string List2Json(List<object> list)
        {
            return "[" + string.Join(",", list.Select(x => Object2Json(x))) + "]";
        }

        private static string Array2Json(object[] array)
        {
            return "[" + string.Join(",", array.Select(x => Object2Json(x))) + "]";
        }

        /// <summary>
        /// Serialize an object into JSON string
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        public static string Object2Json(object obj)
        {
            return obj switch
            {
                // Nested object
                Dictionary<string, object> dict => Dictionary2Json(dict)!,
                // Object list
                List<object> list          => List2Json(list),
                // Object array
                object[] array             => Array2Json(array),
                // User-defined json serialization
                IJSONSerializable objValue => objValue.ToJson()!,
                // String value, wrap with quoatation marks
                string strValue            => $"\"{strValue}\"",
                // Boolean value, should be lowercase 'true' or 'false'
                bool boolValue             => boolValue ? "true" : "false",
                // Other types, just convert to string
                _                          => obj.ToString()!
            };
        }
    }
}
