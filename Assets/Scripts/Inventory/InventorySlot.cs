using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System;

//为什么用Struct:struct是值类型，NetworkList直接存值，不需要new,不产生GC
[System.Serializable]
public struct InventorySlot : INetworkSerializable,IEquatable<InventorySlot>
{
    public int itemId;
    public int count;
    
    public bool IsEmpty=>itemId==0||count<=0;

    public bool Equals(InventorySlot other)
    {
        return itemId == other.itemId && count == other.count;   
    }
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) 
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref itemId );
        serializer.SerializeValue(ref count );
    }
}
