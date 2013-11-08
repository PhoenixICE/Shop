using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using Newtonsoft.Json;

namespace Shop
{
    internal class ShopData
    {
        private Shop main;
        public List<string> name = new List<string>();
        public List<int> price = new List<int>();
        public List<List<Region>> region = new List<List<Region>>();
        public List<List<Group>> group = new List<List<Group>>();
        public List<int> restockTimer = new List<int>();
        public List<int> stock = new List<int>();
        public List<string> onsale = new List<string>();

        public void updateShopData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM store.shop");
            using (result)
            {
                while (result.Read())
                {
                    this.name.Add(result.Get<string>("name"));
                    this.price.Add(result.Get<int>("price"));
                    List<Region> tempList = new List<Region>();
                    foreach (string str in result.Get<string>("region").Split(new Char[] { ',' }).ToList())
                    {
                        tempList.Add(TShock.Regions.GetRegionByName(str));
                    }
                    this.region.Add(tempList);
                    List<Group> tempList2 = new List<Group>();
                    foreach (string str in result.Get<string>("group").Split(new Char[] { ',' }).ToList())
                    {
                        tempList2.Add(TShock.Groups.GetGroupByName(str));
                    }
                    this.group.Add(tempList2);
                    this.restockTimer.Add(result.Get<int>("restockTimer"));
                    this.stock.Add(result.Get<int>("stock"));
                    this.onsale.Add(result.Get<string>("onsale"));
                }
            }
            result.Dispose();
        }
        public ShopData(Shop instance)
        {
            main = instance;
            updateShopData();
        }

    }
}
