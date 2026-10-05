#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ARPG.Data
{
    /// <summary>
    /// Copies the persisted shape, not runtime objects. Leaves are immutable scalars; all
    /// objects, arrays, dictionaries and sets become exclusively owned DTO containers.
    /// Contract getters and ShouldSerialize methods (e.g. EquipmentData basic item omission) run only during capture.
    /// Member-level DefaultValueHandling.Ignore is honored like the Newtonsoft writer (default values are omitted).
    /// No JSON, JToken, table reference or Unity object is handed to the writer.
    /// </summary>
    public sealed class SaveDtoSnapshot
    {
        private readonly DefaultContractResolver _contracts = new();
        // 멤버별 기본값 (DefaultValueHandling.Ignore 판정용). 계약과 함께 캐시되는 읽기 전용 설정값이다.
        private readonly Dictionary<JsonProperty, object?> _defaultValues = new();

        public object? Capture(object? source)
        {
            return Copy(source, new HashSet<object>(ReferenceIdentity.Instance), 0);
        }

        public ulong GetGroupMask(object source, Func<string, ulong> getGroup)
        {
            var contract = (JsonObjectContract)_contracts.ResolveContract(source.GetType());
            ulong groups = 0;
            for (int i = 0; i < contract.Properties.Count; i++)
                if (contract.Properties[i].Ignored == false && contract.Properties[i].Readable == true)
                    groups |= getGroup(contract.Properties[i].UnderlyingName!);
            return groups;
        }

        /// <summary>선택 묶음만 읽는다. 준비가 모두 성공한 뒤 적용하며 생략 키 제거와 계약 순서를 보존한다.</summary>
        public void UpdateGroup(object source, Dictionary<string, object?> target, ulong group,
            Func<string, ulong> getGroup)
        {
            if (_contracts.ResolveContract(source.GetType()) is not JsonObjectContract contract || contract.Converter != null)
                throw new InvalidOperationException("Unsupported grouped save DTO: " + source.GetType().FullName);
            Dictionary<string, object?> prepared = new(StringComparer.Ordinal);
            HashSet<object> ancestors = new(ReferenceIdentity.Instance) { source };
            for (int i = 0; i < contract.Properties.Count; i++)
            {
                JsonProperty property = contract.Properties[i];
                if (property.Ignored == true || property.Readable == false ||
                    (getGroup(property.UnderlyingName!) & group) == 0) continue;
                if (property.Converter != null || property.ItemConverter != null)
                    throw new InvalidOperationException("Unsupported save member converter: " + property.PropertyName);
                if (property.ShouldSerialize != null && property.ShouldSerialize(source) == false) continue;
                object? value = property.ValueProvider!.GetValue(source);
                if (IsIgnoredDefault(property, value) == true) continue;
                target.TryGetValue(property.PropertyName!, out object? previous);
                prepared.Add(property.PropertyName!, CopyCollectionChanges(value, previous, ancestors));
            }
            // Reorder only the shallow field table. Retained groups keep their independent containers.
            Dictionary<string, object?> ordered = new(target.Count + prepared.Count, StringComparer.Ordinal);
            for (int i = 0; i < contract.Properties.Count; i++)
            {
                JsonProperty property = contract.Properties[i];
                if (property.Ignored == true || property.Readable == false) continue;
                string name = property.PropertyName!;
                Dictionary<string, object?> values = (getGroup(property.UnderlyingName!) & group) != 0 ? prepared : target;
                if (values.TryGetValue(name, out object? value) == true) ordered.Add(name, value);
            }
            target.Clear();
            foreach (var pair in ordered) target.Add(pair.Key, pair.Value);
        }

        /// <summary>슬롯/청크/오브젝트 목록은 순서를 조립하고 실제 바뀐 항목만 독립 복사한다.</summary>
        private object? CopyCollectionChanges(object? live, object? previous, HashSet<object> ancestors)
        {
            if (live is IDictionary dictionary && previous is Dictionary<string, object?> oldDictionary)
            {
                if (ancestors.Add(live) == false) throw new InvalidOperationException("Cyclic save collection");
                try
                {
                    Dictionary<string, object?> result = new(dictionary.Count, StringComparer.Ordinal);
                    foreach (DictionaryEntry pair in dictionary)
                    {
                        Type keyType = pair.Key.GetType();
                        if (keyType.IsEnum == false && keyType.IsPrimitive == false && pair.Key is string == false)
                            throw new InvalidOperationException("Unsupported save dictionary key: " + keyType.FullName);
                        string key = Convert.ToString(pair.Key, CultureInfo.InvariantCulture)!;
                        oldDictionary.TryGetValue(key, out object? old);
                        result.Add(key, Matches(pair.Value, old, ancestors, 1) == true ? old : Copy(pair.Value, ancestors, 1));
                    }
                    return result;
                }
                finally { ancestors.Remove(live); }
            }
            if (live is IEnumerable sequence && live is string == false && previous is List<object?> oldList)
            {
                if (ancestors.Add(live) == false) throw new InvalidOperationException("Cyclic save collection");
                try
                {
                    List<object?> result = new(live is ICollection collection ? collection.Count : 0);
                    int index = 0;
                    foreach (object? item in sequence)
                    {
                        object? old = index < oldList.Count ? oldList[index] : null;
                        result.Add(Matches(item, old, ancestors, 1) == true ? old : Copy(item, ancestors, 1));
                        index++;
                    }
                    return result;
                }
                finally { ancestors.Remove(live); }
            }
            return Copy(live, ancestors, 1);
        }

        public ulong FindChangedScalarGroups(object source, Dictionary<string, object?> cached, Func<string, ulong> getGroup)
        {
            var contract = (JsonObjectContract)_contracts.ResolveContract(source.GetType());
            ulong groups = 0;
            for (int i = 0; i < contract.Properties.Count; i++)
            {
                JsonProperty property = contract.Properties[i];
                if (property.Ignored == true || property.Readable == false) continue;
                Type? type = property.PropertyType;
                if (type == null || (type.IsPrimitive == false && type.IsEnum == false && type != typeof(string) && type != typeof(decimal))) continue;
                object? value = property.ValueProvider!.GetValue(source);
                bool include = IsIgnoredDefault(property, value) == false && (property.ShouldSerialize?.Invoke(source) ?? true);
                bool existed = cached.TryGetValue(property.PropertyName!, out object? old);
                if (include != existed || (include == true && Equals(value, old) == false)) groups |= getGroup(property.UnderlyingName!);
            }
            return groups;
        }

        private bool Matches(object? source, object? snapshot, HashSet<object> ancestors, int depth)
        {
            if (source == null) return snapshot == null;
            Type type = source.GetType();
            if (type.IsPrimitive == true || type.IsEnum == true || source is string || source is decimal) return source.Equals(snapshot);
            if (depth > 128 || ancestors.Add(source) == false) throw new InvalidOperationException("Cyclic or excessively deep save data");
            try
            {
                if (source is IDictionary dictionary)
                {
                    if (snapshot is not Dictionary<string, object?> old || old.Count != dictionary.Count) return false;
                    foreach (DictionaryEntry pair in dictionary)
                        if (old.TryGetValue(Convert.ToString(pair.Key, CultureInfo.InvariantCulture)!, out object? value) == false || Matches(pair.Value, value, ancestors, depth + 1) == false) return false;
                    return true;
                }
                if (source is IEnumerable sequence)
                {
                    if (snapshot is not List<object?> old) return false;
                    int index = 0;
                    foreach (object? item in sequence)
                    {
                        if (index >= old.Count || Matches(item, old[index], ancestors, depth + 1) == false) return false;
                        index++;
                    }
                    return index == old.Count;
                }
                if (type.Namespace == null || type.Namespace.StartsWith("ARPG", StringComparison.Ordinal) == false ||
                    _contracts.ResolveContract(type) is not JsonObjectContract contract || contract.Converter != null || snapshot is not Dictionary<string, object?> fields) return false;
                int count = 0;
                for (int i = 0; i < contract.Properties.Count; i++)
                {
                    JsonProperty property = contract.Properties[i];
                    if (property.Ignored == true || property.Readable == false) continue;
                    if (property.Converter != null || property.ItemConverter != null) return false;
                    if (property.ShouldSerialize?.Invoke(source) == false) continue;
                    object? value = property.ValueProvider!.GetValue(source);
                    if (IsIgnoredDefault(property, value) == true) continue;
                    count++;
                    if (fields.TryGetValue(property.PropertyName!, out object? old) == false || Matches(value, old, ancestors, depth + 1) == false) return false;
                }
                return count == fields.Count;
            }
            finally { ancestors.Remove(source); }
        }

        private object? Copy(object? source, HashSet<object> ancestors, int depth)
        {
            if (source == null) return null;
            Type type = source.GetType();
            if (type.IsEnum == true || type.IsPrimitive == true || source is string || source is decimal)
                return source;
            if (depth > 128 || ancestors.Add(source) == false)
                throw new InvalidOperationException("Cyclic or excessively deep save data: " + type.FullName);

            try
            {
                if (source is IDictionary dictionary)
                {
                    var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
                    IDictionaryEnumerator entries = dictionary.GetEnumerator();
                    try
                    {
                        for (; entries.MoveNext();)
                        {
                            object key = entries.Key;
                            Type keyType = key.GetType();
                            if (keyType.IsEnum == false && keyType.IsPrimitive == false && key is string == false)
                                throw new InvalidOperationException("Unsupported save dictionary key: " + keyType.FullName);
                            result.Add(Convert.ToString(key, CultureInfo.InvariantCulture)!, Copy(entries.Value, ancestors, depth + 1));
                        }
                    }
                    finally { (entries as IDisposable)?.Dispose(); }
                    return result;
                }

                if (source is IEnumerable sequence)
                {
                    var result = new List<object?>(source is ICollection collection ? collection.Count : 0);
                    IEnumerator entries = sequence.GetEnumerator();
                    try
                    {
                        for (; entries.MoveNext();) result.Add(Copy(entries.Current, ancestors, depth + 1));
                    }
                    finally { (entries as IDisposable)?.Dispose(); }
                    return result;
                }

                // Fail closed on new runtime/reference types instead of accidentally sharing them.
                if (type.Namespace == null || type.Namespace.StartsWith("ARPG", StringComparison.Ordinal) == false ||
                    _contracts.ResolveContract(type) is not JsonObjectContract contract || contract.Converter != null)
                    throw new InvalidOperationException("Unsupported save DTO: " + type.FullName);

                var fields = new Dictionary<string, object?>(contract.Properties.Count, StringComparer.Ordinal);
                for (int i = 0; i < contract.Properties.Count; i++)
                {
                    JsonProperty property = contract.Properties[i];
                    if (property.Ignored == true || property.Readable == false) continue;
                    if (property.Converter != null || property.ItemConverter != null)
                        throw new InvalidOperationException("Unsupported save member converter: " + property.PropertyName);
                    if (property.ShouldSerialize != null && property.ShouldSerialize(source) == false) continue;
                    object? value = property.ValueProvider!.GetValue(source);
                    if (IsIgnoredDefault(property, value) == true) continue;
                    fields.Add(property.PropertyName!, Copy(value, ancestors, depth + 1));
                }
                return fields;
            }
            finally { ancestors.Remove(source); }
        }

        /// <summary>
        /// Newtonsoft 직렬화기와 같은 규칙: 멤버에 DefaultValueHandling.Ignore가 있고 값이 기본값([DefaultValue] 또는
        /// 값 형식의 default)과 같으면 저장하지 않는다. 로드 때는 필드 초기값(또는 Populate의 기본값)으로 돌아온다.
        /// </summary>
        private bool IsIgnoredDefault(JsonProperty property, object? value)
        {
            DefaultValueHandling handling = property.DefaultValueHandling ?? DefaultValueHandling.Include;
            if ((handling & DefaultValueHandling.Ignore) != DefaultValueHandling.Ignore)
                return false;

            if (_defaultValues.TryGetValue(property, out object? defaultValue) == false)
            {
                defaultValue = property.DefaultValue;
                if (defaultValue == null && property.PropertyType != null && property.PropertyType.IsValueType == true)
                    defaultValue = Activator.CreateInstance(property.PropertyType);
                _defaultValues.Add(property, defaultValue);
            }
            return ValueEquals(value, defaultValue);
        }

        private static bool ValueEquals(object? value, object? defaultValue)
        {
            if (value == null || defaultValue == null)
                return value == null && defaultValue == null;
            if (value.GetType() == defaultValue.GetType())
                return value.Equals(defaultValue);
            // [DefaultValue(-1)]을 float 멤버에 다는 식의 숫자 형식 차이는 값으로 비교한다 (Newtonsoft ValueEquals와 같다).
            if (value is IConvertible && defaultValue is IConvertible && value.GetType().IsEnum == false &&
                defaultValue.GetType().IsEnum == false && value is string == false && defaultValue is string == false)
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    .Equals(Convert.ToDouble(defaultValue, CultureInfo.InvariantCulture));
            }
            return false;
        }

        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentity Instance = new();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
