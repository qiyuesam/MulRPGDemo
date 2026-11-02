// Assets/Scripts/Inventory/UI/InventorySlotUI.cs（修改后，改动标 ★）

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Image selectBorder;

    private int slotIndex;
    private IInventoryProvider provider;
    private InventoryUI owner;

    public void Setup(InventoryUI owner, IInventoryProvider inv, int index)
    {
        this.owner = owner;
        provider = inv;
        slotIndex = index;
        Refresh();
    }

    public void Refresh()
    {
        // ★ 改3：inventory → provider
        var slot = provider.GetSlot(slotIndex);
        if (slot.IsEmpty)
        {
            icon.sprite = null;
            icon.color = new Color(1, 1, 1, 0);
            countText.text = "";
        }
        else
        {
            var item = provider.GetItemData(slot.itemId);
            icon.sprite = item?.itemIcon;
            icon.color = Color.white;
            countText.text = slot.count > 1 ? slot.count.ToString() : "";
        }
    }

    public void SetSelected(bool selected)
    {
        selectBorder.gameObject.SetActive(selected);
    }

    public void OnClick()
    {
        owner?.OnSlotClicked(slotIndex);
    }
}