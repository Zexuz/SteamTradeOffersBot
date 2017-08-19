using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading;

namespace SteamAPI
{
    /// <summary>
    /// Generic Steam Backpack Interface
    /// </summary>
    public class GenericInventory
    {
        private readonly BotInventories _inventories = new BotInventories();
        private readonly InventoryTasks _inventoryTasks = new InventoryTasks();
        private readonly Task _constructTask;
        private const int WebRequestMaxRetries = 3;
        private const int WebRequestTimeBetweenRetriesMs = 1000;
        private SteamWeb _steamWeb;
        private bool _loaded = false;

        /// <summary>
        /// Gets the content of all inventories listed in "http://steamcommunity.com/inventory/STEAM_ID_/APP_ID/CONTEXT_ID
        /// </summary>
        public BotInventories Inventories
        {
            get
            {
                WaitAllTasks();
                return _inventories;
            }
        }

        public GenericInventory(
            SteamID steamId,
            SteamWeb steamWeb,
            Dictionary<int, int> appIdsAndContextId,
            int count = 500,
            string lastAssetId = ""
        )
        {
            if (appIdsAndContextId == null)
            {
                throw new ArgumentNullException("appIdsAndContextId");
            }

            if (appIdsAndContextId.Count == 0)
            {
                throw new ArgumentException("must have some value added", "appIdsAndContextId");
            }
            _steamWeb = steamWeb;

            _constructTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    foreach (var kvp in appIdsAndContextId)
                    {
                        var contextId = (ulong) kvp.Value;
                        var appId = kvp.Key;
                        _inventoryTasks[appId] = new InventoryTasks.ContextTask();
                        _inventoryTasks[appId][contextId] = Task.Factory.StartNew(() =>
                        {
                            var inventory = FetchInventory(steamId, appId, contextId, count, lastAssetId);
                            if (!_inventories.HasAppId(appId))
                                _inventories[appId] = new BotInventories.ContextInventory();
                            if (inventory != null && !_inventories[appId].HasContextId(contextId))
                                _inventories[appId].Add(contextId, inventory);
                        });
                    }

                    Success = true;
                }
                catch (Exception ex)
                {
                    Success = false;
                    Console.WriteLine(ex);
                }
            });
            new Thread(WaitAllTasks).Start();
        }

        public static GenericInventory FetchInventories(
            SteamID steamId,
            SteamWeb steamWeb,
            Dictionary<int, int> appIdsAndContextId,
            int count = 500,
            string lastAssetId = ""
        )
        {
            return new GenericInventory(steamId, steamWeb, appIdsAndContextId, count, lastAssetId);
        }

        public enum AppId
        {
            TF2 = 440,
            Dota2 = 570,
            Portal2 = 620,
            CSGO = 730,
            SpiralKnights = 99900,
            H1Z1 = 295110,
            Steam = 753,
            PUBG = 578080
        }

        public enum ContextId
        {
            TF2 = 2,
            Dota2 = 2,
            Portal2 = 2,
            CSGO = 2,
            H1Z1 = 1,
            PUBG = 2,
            SteamGifts = 1,
            SteamCoupons = 3,
            SteamCommunity = 6,
            SteamItemRewards = 7
        }

        /// <summary>
        /// Use this to iterate through items in the inventory.
        /// </summary>
        /// <param name="appId">App ID</param>
        /// <param name="contextId">Context ID</param>
        /// <exception cref="GenericInventoryException">Thrown when inventory does not exist</exception>
        /// <returns>An Inventory object</returns>
        public Inventory GetInventory(int appId, ulong contextId)
        {
            try
            {
                return Inventories[appId][contextId];
            }
            catch
            {
                throw new GenericInventoryException();
            }
        }

        public void AddForeignInventory(SteamID steamId, int appId, ulong contextId)
        {
            var inventory = FetchForeignInventory(steamId, appId, contextId);
            if (!_inventories.HasAppId(appId))
                _inventories[appId] = new BotInventories.ContextInventory();
            if (inventory != null && !_inventories[appId].HasContextId(contextId))
                _inventories[appId].Add(contextId, inventory);
        }

        private Inventory FetchForeignInventory(SteamID steamId, int appId, ulong contextId, int count = 5000)
        {
            return FetchInventory(steamId, appId, contextId, count);
        }

        private Inventory FetchInventory(SteamID steamId, int appId, ulong contextId, int count = 5000, string lastAssetId = "")
        {
            var inventoryUrl = string.Format("http://steamcommunity.com/inventory/{0}/{1}/{2}?count={3}", steamId.ConvertToUInt64(), appId, contextId, count);

            if (!string.IsNullOrEmpty(lastAssetId))
                inventoryUrl += "&start_assetid=" + lastAssetId;


            var response = RetryWebRequest(inventoryUrl);
            try
            {
                var inventory = JsonConvert.DeserializeObject<Inventory>(response);
                if (inventory.More)
                {
                    var addInv = FetchInventory(steamId, appId, contextId, count, inventory.LastAssetId);
                    inventory.Items = inventory.Items.Concat(addInv.Items).ToList();
                    inventory.Descriptions = inventory.Descriptions.Concat(addInv.Descriptions).ToList();
                }
                inventory.AppId = appId;
                inventory.ContextId = contextId;
                inventory.SteamId = steamId;
                return inventory;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to deserialize {0}.", inventoryUrl);
                Console.WriteLine(ex);
                return null;
            }
        }

        private void WaitAllTasks()
        {
            _constructTask.Wait();
            foreach (var contextTask in _inventoryTasks.SelectMany(task => task.Value))
            {
                contextTask.Value.Wait();
            }
            OnInventoriesLoaded(EventArgs.Empty);
        }

        public delegate void InventoriesLoadedEventHandler(object sender, EventArgs e);

        public event InventoriesLoadedEventHandler InventoriesLoaded;

        protected virtual void OnInventoriesLoaded(EventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            if (InventoriesLoaded != null)
                InventoriesLoaded(this, e);
        }

        /// <summary>
        /// Calls the given function multiple times, until we get a non-null/non-false/non-zero result, or we've made at least
        /// WEB_REQUEST_MAX_RETRIES attempts (with WEB_REQUEST_TIME_BETWEEN_RETRIES_MS between attempts)
        /// </summary>
        /// <returns>The result of the function if it succeeded, or an empty string otherwise</returns>
        private string RetryWebRequest(string url)
        {
            for (var i = 0; i < WebRequestMaxRetries; i++)
            {
                try
                {
                    return _steamWeb.Fetch(url, "GET");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                if (i != WebRequestMaxRetries)
                {
                    System.Threading.Thread.Sleep(WebRequestTimeBetweenRetriesMs);
                }
            }
            return string.Empty;
        }

        public bool Success = true;
        public bool IsPrivate;

        public class Inventory
        {
            public Item GetItem(ItemDescription itemDescription)
            {
                return Items.SingleOrDefault(item =>
                    itemDescription.AppId.ToString() == item.Appid &&
                    itemDescription.ClassId.ToString() == item.ClassId &&
                    itemDescription.InstanceId.ToString() == item.InstanceId
                );
            }

            public ItemDescription GetItemDescription(Item item)
            {
                if (item == null) return null;

                return Descriptions.FirstOrDefault(itemDesc =>
                    itemDesc.AppId.ToString() == item.Appid &&
                    itemDesc.ClassId.ToString() == item.ClassId &&
                    itemDesc.InstanceId.ToString() == item.InstanceId
                );
            }


            public int AppId { get; set; }
            public ulong ContextId { get; set; }
            public SteamID SteamId { get; set; }

            [JsonProperty("descriptions")]
            public List<ItemDescription> Descriptions { get; set; }

            [JsonProperty("total_inventory_count")]
            public int Count { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("assets")]
            public List<Item> Items { get; set; }

            [JsonProperty("last_assetid")]
            public string LastAssetId { get; set; }

            [JsonProperty("more_items")]
            private int _more { get; set; }

            public bool More
            {
                get { return Convert.ToBoolean(_more); }
                set { _more = Convert.ToInt32(value); }
            }


            public class Item : IEquatable<Item>
            {
                [JsonProperty("appid")]
                public string Appid { get; set; }

                [JsonProperty("contextid")]
                public string Contextid { get; set; }

                [JsonProperty("assetid")]
                private string _assetid { get; set; }

                public ulong AssetId
                {
                    get { return Convert.ToUInt64(_assetid); }
                    set { _assetid = value.ToString(); }
                }

                [JsonProperty("classid")]
                public string ClassId { get; set; }

                [JsonProperty("instanceid")]
                public string InstanceId { get; set; }

                [JsonProperty("amount")]
                public string Amount { get; set; }


                public override bool Equals(object obj)
                {
                    return Equals(obj as Item);
                }

                public override int GetHashCode()
                {
                    return base.GetHashCode();
                }

                public bool Equals(Item other)
                {
                    if (other == null)
                        return false;

                    return Appid == other.Appid &&
                           Contextid == other.Contextid &&
                           AssetId == other.AssetId &&
                           ClassId == other.ClassId &&
                           InstanceId == other.InstanceId &&
                           Amount == other.Amount;
                }
            }

            public class ItemDescription
            {
                [JsonProperty("appid")]
                public int AppId { get; set; }

                [JsonProperty("classid")]
                public ulong ClassId { get; set; }

                [JsonProperty("instanceid")]
                public ulong InstanceId { get; set; }

                [JsonProperty("currency")] private short _currency;

                public bool IsCurrency
                {
                    get { return _currency == 1; }
                    set { _currency = Convert.ToInt16(value); }
                }

                [JsonProperty("icon_url")]
                public string IconUrl { get; set; }

                [JsonProperty("icon_url_large")]
                public string IconUrlLarge { get; set; }

                [JsonProperty("icon_drag_url")]
                public string IconDragUrl { get; set; }

                [JsonProperty("name")]
                public string DisplayName { get; set; }

                [JsonProperty("market_hash_name")]
                public string MarketHashName { get; set; }

                [JsonProperty("market_name")]
                private string name { get; set; }

                public string Name
                {
                    get { return string.IsNullOrEmpty(name) ? DisplayName : name; }
                }

                [JsonProperty("name_color")]
                public string NameColor { get; set; }

                [JsonProperty("background_color")]
                public string BackgroundColor { get; set; }

                [JsonProperty("type")]
                public string Type { get; set; }

                public bool IsCraftable
                {
                    get { return Descriptions.Any(description => description.Value == "( Not Usable in Crafting )"); }
                    set { IsCraftable = value; }
                }

                [JsonProperty("tradable")]
                private short isTradable { get; set; }

                public bool IsTradable
                {
                    get { return isTradable == 1; }
                    set { isTradable = Convert.ToInt16(value); }
                }

                [JsonProperty("marketable")]
                private short isMarketable { get; set; }

                public bool IsMarketable
                {
                    get { return isMarketable == 1; }
                    set { isMarketable = Convert.ToInt16(value); }
                }

                [JsonProperty("commodity")]
                private short isCommodity { get; set; }

                public bool IsCommodity
                {
                    get { return isCommodity == 1; }
                    set { isCommodity = Convert.ToInt16(value); }
                }

                [JsonProperty("market_fee_app")]
                public int MarketFeeApp { get; set; }

                [JsonProperty("descriptions")]
                public Description[] Descriptions { get; set; }

                [JsonProperty("actions")]
                public Action[] Actions { get; set; }

                [JsonProperty("owner_actions")]
                public Action[] OwnerActions { get; set; }

                [JsonProperty("tags")]
                public Tag[] Tags { get; set; }

                public class Description
                {
                    [JsonProperty("type")]
                    public string Type { get; set; }

                    [JsonProperty("value")]
                    public string Value { get; set; }
                }

                public class Action
                {
                    [JsonProperty("name")]
                    public string Name { get; set; }

                    [JsonProperty("link")]
                    public string Link { get; set; }
                }

                public class Tag
                {
                    [JsonProperty("internal_name")]
                    public string InternalName { get; set; }

                    [JsonProperty("name")]
                    public string Name { get; set; }

                    [JsonProperty("category")]
                    public string Category { get; set; }

                    [JsonProperty("color")]
                    public string Color { get; set; }

                    [JsonProperty("category_name")]
                    public string CategoryName { get; set; }
                }

                public class App_Data
                {
                    [JsonProperty("def_index")]
                    public ushort Defindex { get; set; }

                    [JsonProperty("quality")]
                    public int Quality { get; set; }
                }
            }
        }
    }

    public class GenericInventoryException : Exception
    {
    }

    public class InventoriesToFetch : Dictionary<SteamID, List<InventoriesToFetch.InventoryInfo>>
    {
        public class InventoryInfo
        {
            public int AppId { get; set; }
            public ulong ContextId { get; set; }

            public InventoryInfo(int appId, ulong contextId)
            {
                AppId = appId;
                ContextId = contextId;
            }
        }
    }

    public class BotInventories : Dictionary<int, BotInventories.ContextInventory>
    {
        public bool HasAppId(int appId)
        {
            return ContainsKey(appId);
        }

        public class ContextInventory : Dictionary<ulong, GenericInventory.Inventory>
        {
            public ulong ContextId { get; set; }
            public GenericInventory.Inventory Inventory { get; set; }

            public bool HasContextId(ulong contextId)
            {
                return ContainsKey(contextId);
            }
        }
    }

    public class InventoryTasks : Dictionary<int, InventoryTasks.ContextTask>
    {
        public bool HasAppId(int appId)
        {
            return ContainsKey(appId);
        }

        public class ContextTask : Dictionary<ulong, Task>
        {
            public ulong ContextId { get; set; }
            public Task InventoryTask { get; set; }
        }
    }
}