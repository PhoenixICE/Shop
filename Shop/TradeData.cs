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
    internal class TradeData
    {
        private Shop main;
        public List<TradeObj> tradeObj = new List<TradeObj>();
        public List<TradeOfferObj> offerList = new List<TradeOfferObj>();

        //adds new trade entry, and updates interal cached data.
        public void addTrade(string user, int itemid, int stack, int witemid = 0, int wstack = 0)
        {
            main.Database.Query("INSERT INTO store.trade(User, ItemID, Stack, WItemID, WStack) VALUES(@0,@1,@2,@3,@4)",user,itemid,stack,witemid,wstack);
            this.tradeObj.Add(new TradeObj(this.tradeObj[this.tradeObj.Count - 1].ID + 1, user, itemid, stack, witemid, wstack));
        }

        public TradeObj TradeObjByID(int ID)
        {
            foreach(TradeObj obj in tradeObj)
            {
                if (obj.ID == ID)
                {
                    return obj;
                }
            }
            return null;
        }

        public void processTrade(TSPlayer player, TradeObj obj, Item item, int stack)
        {
            main.Database.Query("DELETE * FROM store.trade WHERE ID = {0}", obj.ID);
            tradeObj.Remove(obj);
            main.Database.Query("INESRT INTO store.offer(User, ItemID, Stack, TradeID) VALUES(@0,@1,@2,@3)", player.Name, item.netID, stack, -1);
        }

        public void processOffer(TSPlayer player, TradeOfferObj obj, Item item, int stack)
        {

        }

        public void updateTradeData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM store.trade");
            using (result)
            {
                while (result.Read())
                {
                    this.tradeObj.Add(new TradeObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("WItemID"), result.Get<int>("WStack")));
                }
            }
            result.Dispose();
        }
        public void updateOfferData()
        {
            QueryResult result = main.Database.QueryReader("SELECT * FROM store.offer");
            using (result)
            {
                while (result.Read())
                {
                    this.offerList.Add(new TradeOfferObj(result.Get<int>("ID"), result.Get<string>("User"), result.Get<int>("ItemID"), result.Get<int>("Stack"), result.Get<int>("TradeID")));
                }
            }
            result.Dispose();
        }
        public TradeData(Shop instance)
        {
            main = instance;
            updateTradeData();
            updateOfferData();
        }
    }
}
