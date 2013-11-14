using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TShockAPI;
using TShockAPI.DB;
using Terraria;
using Newtonsoft.Json;
using System.Timers;

namespace Shop
{
    internal class ShopData
    {
        private Shop main;
        private List<ShopObj> Shoplist = new List<ShopObj>();        

        public ShopObj FindShopObjbyItemName(string name)
        {
            foreach (ShopObj obj in Shoplist)
            {
                if (obj.Item.ToLower() == name.ToLower())
                {
                    return obj;
                }
            }
            return null;
        }

        public void lowerStock(ShopObj obj, int stack)
        {
            if (obj.Stock > 0)
            {
                obj.Stock -= stack;
                main.Database.Query("UPDATE storeshop SET stock = @0 WHERE name = @1 AND price = @2", obj.Stock, obj.Item, obj.Price);
            }
        }

        public void updateShopData()
        {
            try
            {
                using (QueryResult result = main.Database.QueryReader("SELECT * FROM storeshop"))
                {
                    while (result.Read())
                    {
                        List<string> strgrouplist = result.Get<string>("groupname").Split(',').ToList();
                        if (strgrouplist.Count() == 1 && strgrouplist[0] == "")
                            strgrouplist = new List<string>();
                        List<string> strregionlist = result.Get<string>("region").Split(',').ToList();
                        if (strregionlist.Count() == 1 && strregionlist[0] == "")
                            strregionlist = new List<string>();
                        this.Shoplist.Add(new ShopObj(result.Get<string>("name"), result.Get<int>("price"), strregionlist, strgrouplist, result.Get<int>("restockTimer"), result.Get<int>("stock"), result.Get<string>("onsale").Split(',').ToList(), result.Get<int>("maxstock")));
                    }
                }
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.ToString());
            }
        }
        public ShopData(Shop instance)
        {
            main = instance;
            updateShopData();
        }

    }
}
