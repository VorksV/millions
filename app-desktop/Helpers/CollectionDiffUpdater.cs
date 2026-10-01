using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VoltrisOptimizer.Helpers
{
    public static class CollectionDiffUpdater
    {
        /// <summary>
        /// Atualiza uma ObservableCollection usando diff: remove itens obsoletos,
        /// atualiza propriedades de itens existentes in-place e adiciona novos.
        /// Minimiza alocações Gen2 e eventos de notificação desnecessários.
        /// </summary>
        public static void DiffUpdate<T, TKey>(
            ObservableCollection<T> collection,
            IEnumerable<T> newItems,
            Func<T, TKey> keySelector,
            Action<T, T>? onUpdate = null)
            where TKey : notnull
        {
            var newList = newItems.ToList();
            var newKeys = new HashSet<TKey>(newList.Select(keySelector));

            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (!newKeys.Contains(keySelector(collection[i])))
                    collection.RemoveAt(i);
            }

            var existingDict = new Dictionary<TKey, T>();
            foreach (var item in collection)
                existingDict[keySelector(item)] = item;

            foreach (var newItem in newList)
            {
                var key = keySelector(newItem);
                if (existingDict.TryGetValue(key, out var existing))
                {
                    onUpdate?.Invoke(existing, newItem);
                }
                else
                {
                    collection.Add(newItem);
                }
            }
        }

        /// <summary>
        /// Para coleções cujos itens NÃO implementam INotifyPropertyChanged.
        /// Substitui itens no mesmo índice em vez de Clear+Add.
        /// </summary>
        public static void ReplaceInPlace<T, TKey>(
            ObservableCollection<T> collection,
            IEnumerable<T> newItems,
            Func<T, TKey> keySelector)
            where TKey : notnull
        {
            var newList = newItems.ToList();
            var newKeys = new HashSet<TKey>(newList.Select(keySelector));

            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (!newKeys.Contains(keySelector(collection[i])))
                    collection.RemoveAt(i);
            }

            var existingDict = new Dictionary<TKey, int>();
            for (int i = 0; i < collection.Count; i++)
                existingDict[keySelector(collection[i])] = i;

            foreach (var newItem in newList)
            {
                var key = keySelector(newItem);
                if (existingDict.TryGetValue(key, out var index))
                {
                    collection[index] = newItem;
                }
                else
                {
                    collection.Add(newItem);
                }
            }
        }
    }
}
