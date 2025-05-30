// Copyright 2024 Confluent Inc.
//
// Licensed under the Apache License, Version 2.0 (the 'License');
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an 'AS IS' BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Refer to LICENSE for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#if NET8_0_OR_GREATER
using System.Buffers.Text;
#endif

namespace Confluent.SchemaRegistry
{
    public static class Utils
    {
        public static bool ListEquals<T>(IList<T> a, IList<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.SequenceEqual(b);
        }

        public static int IEnumerableHashCode<T>(IEnumerable<T> items)
        {
            if (items == null) return 0;

            var hash = 0;

            using (var enumerator = items.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    hash = (hash * 397) ^ (enumerator.Current?.GetHashCode() ?? 0);
                }
            }

            return hash;
        }

        internal static bool IsBase64String(string value)
        {
#if NET8_0_OR_GREATER
            return Base64.IsValid(value);
#else
            try
            {
                _ = Convert.FromBase64String(value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
#endif
        }

        public static void WriteVarint(this Stream stream, uint value)
        {
            WriteUnsignedVarint(stream, (value << 1) ^ (value >> 31));
        }

        /// <remarks>
        ///     Inspired by: https://github.com/apache/kafka/blob/2.5/clients/src/main/java/org/apache/kafka/common/utils/ByteUtils.java#L284
        /// </remarks>
        public static void WriteUnsignedVarint(this Stream stream, uint value)
        {
            while ((value & 0xffffff80) != 0L)
            {
                byte b = (byte)((value & 0x7f) | 0x80);
                stream.WriteByte(b);
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        public static int ReadVarint(this Stream stream)
        {
            var value = ReadUnsignedVarint(stream);
            return (int)((value >> 1) ^ -(value & 1));
        }

        /// <remarks>
        ///     Inspired by: https://github.com/apache/kafka/blob/2.5/clients/src/main/java/org/apache/kafka/common/utils/ByteUtils.java#L142
        /// </remarks>
        public static uint ReadUnsignedVarint(this Stream stream)
        {
            int value = 0;
            int i = 0;
            int b;
            while (true)
            {
                b = stream.ReadByte();
                if (b == -1)
                    throw new InvalidOperationException("Unexpected end of stream reading varint.");
                if ((b & 0x80) == 0)
                {
                    break;
                }

                value |= (b & 0x7f) << i;
                i += 7;
                if (i > 28)
                {
                    throw new OverflowException($"Encoded varint is larger than uint.MaxValue");
                }
            }

            value |= b << i;
            return (uint)value;
        }

        /// <summary>
        /// Converts a Guid to a big-endian byte array.
        /// The standard Guid.ToByteArray() returns a mixed-endian format:
        /// Bytes 0-3: Data1 (little-endian)
        /// Bytes 4-5: Data2 (little-endian)
        /// Bytes 6-7: Data3 (little-endian)
        /// Bytes 8-15: Data4 (sequential/big-endian)
        /// This method rearranges the first 8 bytes to be purely big-endian.
        /// </summary>
        /// <param name="guid">The Guid to convert.</param>
        /// <returns>A 16-byte array representing the Guid in big-endian order.</returns>
        public static byte[] ToBigEndian(this Guid guid)
        {
            // Get the mixed-endian byte array from the Guid struct
            byte[] mixedEndianBytes = guid.ToByteArray();
            byte[] bigEndianBytes = new byte[16];

            // --- Reverse the little-endian parts to make them big-endian ---

            // Data1 (bytes 0-3): Reverse the order
            bigEndianBytes[0] = mixedEndianBytes[3];
            bigEndianBytes[1] = mixedEndianBytes[2];
            bigEndianBytes[2] = mixedEndianBytes[1];
            bigEndianBytes[3] = mixedEndianBytes[0];

            // Data2 (bytes 4-5): Reverse the order
            bigEndianBytes[4] = mixedEndianBytes[5];
            bigEndianBytes[5] = mixedEndianBytes[4];

            // Data3 (bytes 6-7): Reverse the order
            bigEndianBytes[6] = mixedEndianBytes[7];
            bigEndianBytes[7] = mixedEndianBytes[6];

            // Data4 (bytes 8-15): Already in the correct sequential (big-endian) order
            Buffer.BlockCopy(mixedEndianBytes, 8, bigEndianBytes, 8, 8);

            return bigEndianBytes;
        }

        /// <summary>
        /// Creates a Guid from a big-endian byte array.
        /// This method reverses the transformation done by ToBigEndian,
        /// converting the big-endian byte order back to the mixed-endian format
        /// expected by the Guid(byte[]) constructor.
        /// </summary>
        /// <param name="bigEndianBytes">A 16-byte array representing a Guid in big-endian order.</param>
        /// <returns>The Guid represented by the big-endian byte array.</returns>
        /// <exception cref="ArgumentNullException">Thrown if bigEndianBytes is null.</exception>
        /// <exception cref="ArgumentException">Thrown if bigEndianBytes is not 16 bytes long.</exception>
        public static Guid GuidFromBigEndian(byte[] bigEndianBytes)
        {
            if (bigEndianBytes == null)
                throw new ArgumentNullException(nameof(bigEndianBytes));
            if (bigEndianBytes.Length != 16)
                throw new ArgumentException("Byte array must be 16 bytes long.",
                    nameof(bigEndianBytes));

            byte[] mixedEndianBytes = new byte[16];

            // --- Reverse the big-endian parts back to little-endian ---

            // Data1 (bytes 0-3): Reverse the order
            mixedEndianBytes[0] = bigEndianBytes[3];
            mixedEndianBytes[1] = bigEndianBytes[2];
            mixedEndianBytes[2] = bigEndianBytes[1];
            mixedEndianBytes[3] = bigEndianBytes[0];

            // Data2 (bytes 4-5): Reverse the order
            mixedEndianBytes[4] = bigEndianBytes[5];
            mixedEndianBytes[5] = bigEndianBytes[4];

            // Data3 (bytes 6-7): Reverse the order
            mixedEndianBytes[6] = bigEndianBytes[7];
            mixedEndianBytes[7] = bigEndianBytes[6];

            // Data4 (bytes 8-15): Copy directly as they are already in the correct order
            // for the mixed-endian format's latter half.
            Buffer.BlockCopy(bigEndianBytes, 8, mixedEndianBytes, 8, 8);

            // Create the Guid from the correctly formatted mixed-endian byte array
            return new Guid(mixedEndianBytes);
        }

        /// <summary>
        /// Asynchronously transforms each element of an IEnumerable&lt;T&gt;,
        /// passing in its zero-based index and the element itself, using a
        /// Func&lt;int, object, Task&lt;object&gt;&gt;, and returns a List&lt;T&gt; of the transformed elements.
        /// </summary>
        /// <param name="sourceEnumerable">
        ///   An object implementing IEnumerable&lt;T&gt; for some T.
        /// </param>
        /// <param name="indexedTransformer">
        ///   A function that takes (index, element as object) and returns a Task whose Result
        ///   is the new element (as object). The returned object must be castable to T.
        /// </param>
        /// <returns>
        ///   A Task whose Result is a List&lt;T&gt; (boxed as object) containing all transformed elements.
        ///   Await and then cast back to IEnumerable&lt;T&gt; to enumerate.
        /// </returns>
        public static async Task<object> TransformEnumerableAsync(
            object sourceEnumerable,
            Func<int, object, Task<object>> indexedTransformer)
        {
            if (sourceEnumerable == null)
                throw new ArgumentNullException(nameof(sourceEnumerable));
            if (indexedTransformer == null)
                throw new ArgumentNullException(nameof(indexedTransformer));

            // 1. Find the IEnumerable<T> interface on the source object
            var srcType = sourceEnumerable.GetType();
            var enumInterface = srcType
                .GetInterfaces()
                .FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumInterface == null)
                throw new ArgumentException("Source must implement IEnumerable<T>", nameof(sourceEnumerable));

            // 2. Extract the element type T
            var elementType = enumInterface.GetGenericArguments()[0];

            // 3. Build a List<T> at runtime
            var listType   = typeof(List<>).MakeGenericType(elementType);
            var resultList = (IList)Activator.CreateInstance(listType);

            // 4. Kick off all transforms in parallel
            var tasks = new List<Task<object>>();
            int index = 0;
            foreach (var item in (IEnumerable)sourceEnumerable)
            {
                tasks.Add(indexedTransformer(index, item));
                index++;
            }

            // 5. Await them all at once
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 6. Populate the result list in original order
            foreach (var transformed in results)
            {
                resultList.Add(transformed);
            }

            // 7. Return the List<T> as object
            return resultList;
        }

        /// <summary>
        /// Asynchronously transforms each value of an IDictionary&lt;K,V&gt;,
        /// by invoking the provided Func&lt;object, object, Task&lt;object&gt;&gt;
        /// passing in the key and the original value
        /// and returns a new Dictionary&lt;K,V&gt; whose values are the awaited results.
        /// </summary>
        /// <param name="sourceDictionary">
        ///   An object implementing IDictionary&lt;K,V&gt; for some K,V.
        /// </param>
        /// <param name="transformer">
        ///   A function that takes (key as object, value as object) and returns a Task whose Result
        ///   is the new value (as object). The returned object must be castable to V.
        /// </param>
        /// <returns>
        ///   A Task whose Result is a Dictionary&lt;K,V&gt; containing all the transformed values.
        ///   Await and then cast back to IDictionary&lt;K,V&gt; to enumerate.
        /// </returns>
        public static async Task<object> TransformDictionaryAsync(
            object sourceDictionary,
            Func<object, object, Task<object>> transformer)
        {
            if (sourceDictionary == null)
                throw new ArgumentNullException(nameof(sourceDictionary));
            if (transformer == null)
                throw new ArgumentNullException(nameof(transformer));

            // 1. Find the IDictionary<K,V> interface on the source object
            var srcType = sourceDictionary.GetType();
            var dictInterface = srcType
                .GetInterfaces()
                .FirstOrDefault(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

            if (dictInterface == null)
                throw new ArgumentException("Source must implement IDictionary<K,V>", nameof(sourceDictionary));

            // 2. Extract K and V
            var genericArgs = dictInterface.GetGenericArguments();
            var keyType   = genericArgs[0];
            var valueType = genericArgs[1];

            // 3. Create a Dictionary<K,V> at runtime
            var resultDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            var resultDict     = (IDictionary)Activator.CreateInstance(resultDictType);

            // 4. Enumerate the source via the non‐generic IDictionary interface
            var nonGenericDict = (IDictionary)sourceDictionary;
            var keys       = new List<object>();
            var tasks      = new List<Task<object>>();

            foreach (DictionaryEntry entry in nonGenericDict)
            {
                keys.Add(entry.Key);
                tasks.Add(transformer(entry.Key, entry.Value));
            }

            // 5. Await all transformations
            var transformedValues = await Task.WhenAll(tasks).ConfigureAwait(false);

            // 6. Reconstruct the new dictionary in original order
            for (int i = 0; i < keys.Count; i++)
            {
                resultDict.Add(keys[i], transformedValues[i]);
            }

            // 7. Return boxed Dictionary<K,V>
            return resultDict;
        }
    }
}
