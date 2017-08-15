using System;
using Newtonsoft.Json;

namespace SteamAPI
{
    public class NewGenericInventory
    {
        public Item[] Items { get; private set; }
        public bool IsGood { get; private set; }


        public NewGenericInventory()
        {
            IsGood = false;
        }

        protected NewGenericInventory(Item[] items, int resultSuccess)
        {
            IsGood = resultSuccess == 1;
            Items = items;
        }

        public static NewGenericInventory GetInventory(ulong steamId, SteamWeb steamWeb, int appId)
        {
            try
            {
                var url = string.Format("http://steamcommunity.com/inventory/{0}/{1}/2?trading=1",steamId,appId);
                var rawResponse = steamWeb.Fetch(url, "GET", null, false);
                var response = JsonConvert.DeserializeObject<InventoryResponse>(rawResponse);

                var items = MapInventoryResponse(response);

                return new NewGenericInventory(items, response.Success);
            }
            catch (Exception e)
            {
                return new NewGenericInventory(null, 0);
            }
        }

        private static Item[] MapInventoryResponse(InventoryResponse response)
        {
            var items = new Item[response.Assets.Length];
            for (var index = 0; index < response.Assets.Length; index++)
            {
                var asset = response.Assets[index];
                foreach (var description in response.Descriptions)
                {
                    if (asset.Classid != description.Classid || asset.Instanceid != description.Instanceid) continue;
                    var item = new Item
                    {
                        Appid = description.Appid,
                        Appnameid = description.Appnameid,
                        AssetId = asset.Assetid,
                        BackgroundColor = description.BackgroundColor,
                        Classid = description.Classid,
                        Commodity = description.Commodity,
                        Currency = description.Currency,
                        IconUrl = description.IconUrl,
                        Instanceid = description.Instanceid,
                        Marketable = description.Marketable,
                        MarketHashName = description.MarketHashName,
                        MarketName = description.MarketName,
                        MarketTradableRestriction = description.MarketTradableRestriction,
                        NameColor = description.NameColor,
                        Tradable = description.Tradable,
                        Type = description.Type
                    };
                    items[index] = item;
                }
            }
            return items;
        }

        protected class InventoryResponse
        {
            public Asset[] Assets { get; set; }
            public Description[] Descriptions { get; set; }

            [JsonProperty("total_inventory_count")]
            public int Count { get; set; }

            public int Success { get; set; }
        }
        
      
       
    }
    public class Item : Description
    {
        public string AssetId { get; set; }
    }
    
    public class Asset
    {
        [JsonProperty("appid")]
        public string Appid { get; set; }

        [JsonProperty("contextid")]
        public string Contextid { get; set; }

        [JsonProperty("assetid")]
        public string Assetid { get; set; }

        [JsonProperty("classid")]
        public string Classid { get; set; }

        [JsonProperty("instanceid")]
        public string Instanceid { get; set; }

        [JsonProperty("amount")]
        public string Amount { get; set; }
    }

    public class Description
    {
        [JsonProperty("appid")]
        public int Appid { get; set; }

        [JsonProperty("classid")]
        public string Classid { get; set; }

        [JsonProperty("instanceid")]
        public string Instanceid { get; set; }

        [JsonProperty("currency")]
        public int Currency { get; set; }

        [JsonProperty("background_color")]
        public string BackgroundColor { get; set; }

        [JsonProperty("icon_url")]
        public string IconUrl { get; set; }

        [JsonProperty("tradable")]
        public bool Tradable { get; set; }

        [JsonProperty("name")]
        public string Appnameid { get; set; }

        [JsonProperty("name_color")]
        public string NameColor { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("market_name")]
        public string MarketName { get; set; }

        [JsonProperty("market_hash_name")]
        public string MarketHashName { get; set; }

        [JsonProperty("commodity")]
        public int Commodity { get; set; }

        [JsonProperty("market_tradable_restriction")]
        public int MarketTradableRestriction { get; set; }

        [JsonProperty("marketable")]
        public int Marketable { get; set; }
    }
}