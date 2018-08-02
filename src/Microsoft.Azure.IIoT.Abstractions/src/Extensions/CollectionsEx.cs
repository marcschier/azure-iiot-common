// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace System.Collections.Generic {
    using System;
    using System.Linq;
    using System.Collections;
    using System.Reflection;

    public static class CollectionsEx {

        /// <summary>
        /// Safe hash
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="seq"></param>
        /// <returns></returns>
        public static int GetHashCodeSafe<T>(this IEnumerable<T> seq) =>
            GetHashCodeSafe(seq, EqualityComparer<T>.Default.GetHashCode);

        /// <summary>
        /// Safe hash
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="seq"></param>
        /// <returns></returns>
        public static int GetHashCodeSafe<T>(this IEnumerable<T> seq, Func<T, int> hash) {
            var hashCode = -932366343;
            if (seq != null) {
                foreach (var item in seq) {
                    hashCode = hashCode * -1521134295 + hash(item);
                }
            }
            return hashCode;
        }

        /// <summary>
        /// Safe sequence equals
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="seq"></param>
        /// <param name="that"></param>
        /// <returns></returns>
        public static bool SequenceEqualsSafe<T>(this IEnumerable<T> seq,
            IEnumerable<T> that) {
            if (seq == that) {
                return true;
            }
            if (seq == null || that == null) {
                return false;
            }
            return seq.SequenceEqual(that);
        }

        /// <summary>
        /// Safe set equals
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="seq"></param>
        /// <param name="that"></param>
        /// <param name="func"></param>
        /// <returns></returns>
        public static bool SetEqualsSafe<T>(this IEnumerable<T> seq, IEnumerable<T> that,
            Func<T, T, bool> func) {
            if (seq == that) {
                return true;
            }
            if (seq == null || that == null) {
                return false;
            }
            var source = new HashSet<T>(seq, Compare.Using(func));
            return source.SetEquals(that);
        }
    }
}