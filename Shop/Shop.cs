﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Data;
using System.IO;
using System.Text;
using System.Net;

// Terraria related API References
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;

using TerrariaApi.Server;
using Terraria;

using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Economy;
using Wolfje.Plugins.SEconomy.Journal;


namespace Shop
{
    [ApiVersion(1, 14)]
    public class Shop : TerrariaPlugin
    {
        internal ShopData ShopList;
        internal TradeData TradeList;
        public IDbConnection Database;
        public String SavePath = TShock.SavePath;
        public ShopConfig configObj { get; set; }
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "store.json"); } }

        public override string Author
        {
            get { return "IcyPhoenix"; }
        }
        public override string Description
        {
            get { return "SEconomy based Shop"; }
        }

        public override string Name
        {
            get { return "Shop"; }
        }

        public override Version Version
        {
            get { return new Version("1.0"); }
        }
        public Shop(Main game)
            : base(game)
        {
        }
        public override void Initialize()
        {
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                Database.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnInitialize(EventArgs args)
        {
            configObj = new ShopConfig();
            SetupConfig();
            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] host = TShock.Config.MySqlHost.Split(':');
                    Database = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                        host[0],
                        host.Length == 1 ? "3306" : host[1],
                        TShock.Config.MySqlDbName,
                        TShock.Config.MySqlUsername,
                        TShock.Config.MySqlPassword)

                    };
                    break;
                case "sqlite":

                    // Create folder if it does not exist. (SQLITE Specific.)
                    if (!System.IO.Directory.Exists(SavePath))
                    {
                        System.IO.Directory.CreateDirectory(SavePath);
                    }


                    string sql = Path.Combine(SavePath, "store.sqlite");
                    Database = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;
            }
            SqlTableCreator sqlcreator = new SqlTableCreator(Database, Database.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            sqlcreator.EnsureExists(new SqlTable("storeshop",
                new SqlColumn("name", MySqlDbType.VarChar) { Primary = true, Length = 30 },
                new SqlColumn("price", MySqlDbType.Int32),
                new SqlColumn("region", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("groupname", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("restockTimer", MySqlDbType.Int32),
                new SqlColumn("stock", MySqlDbType.Int32),
                new SqlColumn("onsale", MySqlDbType.VarChar) { Length = 30 }
                ));
            sqlcreator.EnsureExists(new SqlTable("storetrade",
                new SqlColumn("ID", MySqlDbType.VarChar) { Primary = true, Length = 30 },
                new SqlColumn("User", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("ItemID", MySqlDbType.Int32),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("WItemID", MySqlDbType.Int32),
                new SqlColumn("WStack", MySqlDbType.Int32)
                ));
            sqlcreator.EnsureExists(new SqlTable("storeoffer",
                new SqlColumn("ID", MySqlDbType.VarChar) { Primary = true, Length = 30 },
                new SqlColumn("User", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("ItemID", MySqlDbType.Int32),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("TradeID", MySqlDbType.Int32)
                ));

            ShopList = new ShopData(this);
            TradeList = new TradeData(this);

            Commands.ChatCommands.Add(new Command("store.shop", shop, "shop"));
            Commands.ChatCommands.Add(new Command("store.admin", shopreload, "reloadstore"));
            Commands.ChatCommands.Add(new Command("store.trade", trade, "trade"));
        }
        //trade {market} {item} {amount} {item} {amount}
        private void trade(CommandArgs args)
        {
            try
            {
                //list help
                if (args.Parameters.Count == 0)
                {
                    args.Player.SendInfoMessage("Info: Type the below commands for more info");
                    args.Player.SendInfoMessage("Info: /trade add");
                    args.Player.SendInfoMessage("Info: /trade offer");
                    args.Player.SendInfoMessage("Info: /trade accept");
                    args.Player.SendInfoMessage("Info: /trade list");
                    args.Player.SendInfoMessage("Info: /trade collect");
                    args.Player.SendInfoMessage("Info: /trade check");
                    return;
                }
                //main args switch
                string Switch = args.Parameters[0].ToLower();
                if (Switch == "add")
                {
                    if (!args.Player.Group.HasPermission("shop.trade.add"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to add trades!");
                        return;
                    }
                    //display help as no item specificed | or too many params
                    if (args.Parameters.Count == 1 || args.Parameters.Count >= 6)
                    {
                        args.Player.SendInfoMessage("Info: /trade add {item} {amount} [item] [amount]");
                        args.Player.SendInfoMessage("Info: Set the item you wish to trade and the amount of them, optionally set the item you wish to trade for and the amount of them");
                        return;
                    }
                    //Trading with minimum required amount of args
                    if (args.Parameters.Count == 3)
                    {
                        int stack;
                        if (!int.TryParse(args.Parameters[2], out stack))
                        {
                            args.Player.SendInfoMessage("Info: /trade add {item} {amount} [item] [amount]");
                            args.Player.SendInfoMessage("Info: Set the item you wish to trade and the amount of them, optionally set the item you wish to trade for and the amount of them");
                            return;
                        }
                        if (stack == 0)
                        {
                            args.Player.SendErrorMessage("Error: Can't have stack size of zero");
                            return;
                        }
                        Item item = getItem(args.Player, args.Parameters[1], 1);
                        if (item == null)
                        {
                            args.Player.SendErrorMessage("Error: Incorrect item name or ID, please use quotes if the item has a space in it");
                            return;
                        }
                        for (int i = 0; i < 48; i++)
                        {
                            if (args.TPlayer.inventory[i].netID == item.netID)
                            {
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    //all conditions met, delete item and add offer entry.   
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    AddTrade(args.Player, item, stack);
                                    args.Player.SendInfoMessage("Sucess: Add Trade Successfully! You have Added {0} {1}(s).", stack, item.name);
                                    return;
                                }
                                else
                                {
                                    //failed cause not enough stacks from player
                                    args.Player.SendErrorMessage("Error: Offer could not be completed, you have offered more stacks then you have!");
                                    return;
                                }
                            }
                        }
                        args.Player.SendErrorMessage("Error: Offer could not be completed, you do not have that item to offer");
                        args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                        return;
                    }
                    //Trading with maximum required amount of args
                    //trade add item stack witem wstack
                    if (args.Parameters.Count == 5)
                    {
                        int stack;
                        if (!int.TryParse(args.Parameters[2], out stack))
                        {
                            args.Player.SendInfoMessage("Info: /trade add {item} {amount} [item] [amount]");
                            args.Player.SendInfoMessage("Info: Set the item you wish to trade and the amount of them, optionally set the item you wish to trade for and the amount of them");
                        }
                        if (stack == 0)
                        {
                            args.Player.SendErrorMessage("Error: Can't have stack size of zero");
                            return;
                        }
                        Item item = getItem(args.Player, args.Parameters[1], 1);
                        int wstack;
                        if (!Int32.TryParse(args.Parameters[4], out wstack))
                        {
                            args.Player.SendInfoMessage("Info: /trade add {item} {amount} [item] [amount]");
                            args.Player.SendInfoMessage("Info: Set the item you wish to trade and the amount of them, optionally set the item you wish to trade for and the amount of them");
                        }
                        if (wstack == 0)
                        {
                            args.Player.SendErrorMessage("Error: Can't have stack size of zero");
                            return;
                        }
                        Item witem = getItem(args.Player, args.Parameters[3], 1);
                        for (int i = 0; i < 48; i++)
                        {
                            if (args.TPlayer.inventory[i].netID == item.netID)
                            {
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    //all conditions met, delete item and add offer entry.   
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    AddTrade(args.Player, item, stack, witem, wstack);
                                    args.Player.SendInfoMessage("Sucess: Add Trade Successfully! You have Added {0} {1}(s).", stack, item.name);
                                    args.Player.SendInfoMessage("Sucess: Trading for {0} {1}(s).", wstack, witem.name);
                                    return;
                                }
                                else
                                {
                                    //failed cause not enough stacks from player
                                    args.Player.SendErrorMessage("Error: Offer could not be completed, you have offered more stacks then you have!");
                                    return;
                                }
                            }
                        }
                        args.Player.SendErrorMessage("Error: Offer could not be completed, you do not have that item to offer");
                        args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                        return;
                    }
                    return;
                }
                else if (Switch == "list")
                {
                    if (!args.Player.Group.HasPermission("store.trade.list"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to list trades!");
                        return;
                    }
                    //List first 7 pages
                    if (args.Parameters.Count <= 2)
                    {
                        int page;
                        if (args.Parameters.Count == 2)
                        {
                            if (int.TryParse(args.Parameters[1], out page))
                            {
                                page *= 9;
                            }
                        }
                        else
                        {
                            page = 9;
                        }
                        args.Player.SendInfoMessage("ID - User - Item:Stack - Wanted:Stack");
                        for (int i = page - 9; i < page; i++)
                        {
                            if (i > TradeList.tradeObj.Count - 1)
                                break;
                            args.Player.SendInfoMessage("{0} - {1} - {2}:{3} - {4}:{5}", TradeList.tradeObj[i].ID, TradeList.tradeObj[i].User, TShock.Utils.GetItemById(TradeList.tradeObj[i].ItemID).name, TradeList.tradeObj[i].Stack, TShock.Utils.GetItemById(TradeList.tradeObj[i].WItemID).name, TradeList.tradeObj[i].WStack);
                        }
                        args.Player.SendInfoMessage("/trade list {0}", (page / 9) + 1);
                        return;
                    }
                }
                else if (Switch == "accept")
                {
                    if (!args.Player.Group.HasPermission("store.trade.accept"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to accept trades!");
                        return;
                    }
                    //display help as no item specificed | or too many params
                    if (args.Parameters.Count == 1 || args.Parameters.Count >= 3)
                    {
                        args.Player.SendInfoMessage("Info: /trade accept {id}");
                        args.Player.SendInfoMessage("Info: use /trade list, to accept lists of trades");
                        return;
                    }
                    //Trading with required amount of args
                    if (args.Parameters.Count == 2)
                    {
                        int id;
                        if (!int.TryParse(args.Parameters[1], out id))
                        {
                            args.Player.SendErrorMessage("Error: Inccorect ID entered!");
                            return;
                        }
                        TradeObj obj = TradeList.TradeObjByID(id);
                        if (obj == null)
                        {
                            args.Player.SendErrorMessage("Error: Could not locate the Trade ID!");
                            return;
                        }
                        //check if wanted item is listed, otherwise exit
                        if (obj.WItemID == 0)
                        {
                            args.Player.SendErrorMessage("Error: Cannot accept trade as no wanted item is listed.");
                            args.Player.SendErrorMessage("Error: Please use /trade offer, to make an offer to the player.");
                            return;
                        }
                        Item witem = TShock.Utils.GetItemById(obj.WItemID);
                        int wstack = obj.WStack;
                        //check if player actually has the item requested
                        for (int i = 0; i < 48; i++)
                        {
                            if (args.TPlayer.inventory[i].netID == witem.netID)
                            {
                                if (args.TPlayer.inventory[i].stack == wstack)
                                {
                                    //all conditions met, delete item and delete entry, add witem as the prize
                                    TradeList.processTrade(args.Player, obj, witem, wstack);
                                    args.TPlayer.inventory[i].SetDefaults(obj.ItemID);
                                    args.TPlayer.inventory[i].stack = obj.Stack;
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    args.Player.SendInfoMessage("Sucess: Trade Completed Successfully! You have gained {0} of {1}.", obj.Stack, TShock.Utils.GetItemById(obj.ItemID).name);
                                    return;
                                }
                                else
                                {
                                    //failed cause not enough stacks from player
                                    args.Player.SendErrorMessage("Error: Trade could not be completed, you do not have enough stacks of that item");
                                    args.Player.SendErrorMessage("Error: Total Required Amount - {0}", wstack);
                                    return;
                                }
                            }
                        }
                        args.Player.SendErrorMessage("Error: Trade could not be completed, you do not have the required item");
                        args.Player.SendErrorMessage("Error: Required Item - {0}", witem.name);
                        return;
                    }
                }
                //trade offer {id} {item} {stack}
                else if (Switch == "offer")
                {
                    if (args.Parameters.Count != 4)
                    {
                        args.Player.SendInfoMessage("Info: /trade offer {id} {item} {stack}");
                        args.Player.SendInfoMessage("Info: use /trade list, to find lists of ID to trade");
                        return;
                    }
                    if (args.Parameters.Count == 4)
                    {
                        int id;
                        int stack;
                        if (!int.TryParse(args.Parameters[1], out id))
                        {
                            args.Player.SendErrorMessage("Error: Invalid ID Entered!");
                            return;
                        }
                        if (!int.TryParse(args.Parameters[3], out stack))
                        {
                            args.Player.SendErrorMessage("Error: Invalid Stack Entered!");
                            return;
                        }
                        Item item = getItem(args.Player, args.Parameters[2], stack);
                        for (int i = 0; i < 48; i++)
                        {
                            if (args.TPlayer.inventory[i].netID == item.netID)
                            {
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    //all conditions met, delete item and add offer entry.   
                                    TradeList.processOffer(args.Player, id, item.netID, stack);
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    args.Player.SendInfoMessage("Sucess: Offer Completed Successfully! You have offered {0} of {1}.", stack, item.name);
                                    return;
                                }
                                else
                                {
                                    //failed cause not enough stacks from player
                                    args.Player.SendErrorMessage("Error: Offer could not be completed, you have offered more stacks then you have!");
                                    return;
                                }
                            }
                        }
                        args.Player.SendErrorMessage("Error: Offer could not be completed, you do not have that item to offer");
                        args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                        return;
                    }
                }
                else if (Switch == "check")
                {
                    if (args.Parameters.Count >= 3)
                    {
                        args.Player.SendInfoMessage("Info: /trade check [id]");
                        args.Player.SendInfoMessage("Info: Lists your trades, or checks offers on an ID");
                        return;
                    }
                    //List trades
                    if (args.Parameters.Count == 1)
                    {
                        args.Player.SendInfoMessage("ID - User - Item:Stack - Wanted:Stack");
                        foreach (TradeObj obj in TradeList.tradeObj)
                        {
                            if (obj.User == args.Player.Name)
                                args.Player.SendInfoMessage("{0} - {1} - {2}:{3} - {4}:{5}", obj.ID, obj.User, TShock.Utils.GetItemById(obj.ItemID).name, obj.Stack, TShock.Utils.GetItemById(obj.WItemID).name, obj.WStack);
                        }
                        return;
                    }
                    //List offers
                    if (args.Parameters.Count == 2)
                    {
                        int id;
                        if (!int.TryParse(args.Parameters[1], out id))
                        {
                            args.Player.SendErrorMessage("Error: Invalid ID Entered!");
                            return;
                        }
                        if (TradeList.TradeObjByID(id).User != args.Player.Name && !args.Player.Group.HasPermission("store.admin"))
                        {
                            args.Player.SendErrorMessage("Error: You cannot check offers on this item!");
                            return;
                        }
                        args.Player.SendInfoMessage("ID - User - Offered Item:Stack");
                        foreach (OfferObj obj in TradeList.offerObj)
                        {
                            if (obj.Type == id)
                                args.Player.SendInfoMessage("{0} - {1} - {2}:{3}", obj.ID, obj.User, TShock.Utils.GetItemById(obj.ItemID).name, obj.Stack);
                        }
                        return;
                    }
                }
                else if (Switch == "collect")
                {
                    if (args.Parameters.Count() != 2)
                    {
                        args.Player.SendInfoMessage("Info: /trade collect {id|all}");
                        args.Player.SendInfoMessage("Info: Collects traded items from trades, and any finished/rejected trades");
                        args.Player.SendInfoMessage("Info: Specifying id accepts that trade deal - use /trade check to find ids");
                        return;
                    }
                    if (args.Parameters.Count() == 2)
                    {
                        if (args.Parameters[1] == "all")
                        {
                            for (int i2 = 0; i2 < TradeList.offerObj.Count(); i2++)
                            {
                                OfferObj obj = TradeList.offerObj[i2];
                                if (obj != null)
                                {
                                    if (obj.User == args.Player.Name && obj.Type == -1)
                                    {
                                        bool recived = false;
                                        for (int i = 0; i < 48; i++)
                                        {
                                            if (args.TPlayer.inventory[i].netID == 0)
                                            {
                                                //all conditions met, delete item and add offer entry.   
                                                args.TPlayer.inventory[i].SetDefaults(obj.ItemID);
                                                args.TPlayer.inventory[i].stack = obj.Stack;
                                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                                NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                                TradeList.processAccept(args.Player, obj);
                                                args.Player.SendInfoMessage("Sucess: Item Collected! You have Gained {0} of {1}.", obj.Stack, TShock.Utils.GetItemById(obj.ItemID).name);
                                                recived = true;
                                                i2 -= 1;
                                                break;
                                            }
                                        }
                                        if (!recived)
                                        {
                                            args.Player.SendErrorMessage("Error: You have no free inventory spaces!");
                                            return;
                                        }
                                    }
                                }
                            }
                            args.Player.SendInfoMessage("Info: All Items Recieved!");
                        }
                        else
                        {
                            int id;
                            if (!int.TryParse(args.Parameters[1], out id))
                            {
                                args.Player.SendErrorMessage("Error: Invalid ID Entered!");
                                return;
                            }
                            OfferObj oObj = TradeList.OfferObjByID(id);
                            if (oObj == null)
                            {
                                args.Player.SendErrorMessage("Error: Incorrect ID Entered!");
                                return;
                            }
                            TradeObj tObj = TradeList.TradeObjByID(oObj.Type);
                            if (tObj == null)
                            {
                                args.Player.SendErrorMessage("Error: Something is really wrong with the databse - transaction ID: {0} does not exist!", oObj.Type);
                                return;
                            }
                            if (args.Player.Name != tObj.User)
                            {
                                args.Player.SendErrorMessage("Error: You are not the owner of the trade!");
                                return;
                            }
                            //all checks finally passed
                            bool recived = false;
                            for (int i = 0; i < 48; i++)
                            {
                                if (args.TPlayer.inventory[i].netID == 0)
                                {
                                    //all conditions met, delete item and add offer entry.   
                                    args.TPlayer.inventory[i].SetDefaults(oObj.ItemID);
                                    args.TPlayer.inventory[i].stack = oObj.Stack;
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    int tradeID = oObj.Type;
                                    TradeList.processAccept(args.Player, oObj);
                                    args.Player.SendInfoMessage("Sucess: Item Collected! You have Gained {0} of {1}.", oObj.Stack, TShock.Utils.GetItemById(oObj.ItemID).name);
                                    recived = true;
                                    //remove all other offers and return to owner
                                    foreach(OfferObj obj in TradeList.offerObj)
                                    {
                                        if (obj.Type == tradeID)
                                        {
                                            TradeList.returnOffer(obj);
                                            obj.Type = -1;
                                        }
                                    }
                                    break;
                                }
                            }
                            if (!recived)
                            {
                                args.Player.SendErrorMessage("Error: You have no free inventory spaces!");
                                return;
                            }
                            return;
                        }
                    }
                }
                else
                {
                    args.Player.SendInfoMessage("Info: Type the below commands for more info");
                    args.Player.SendInfoMessage("Info: /trade add");
                    args.Player.SendInfoMessage("Info: /trade offer");
                    args.Player.SendInfoMessage("Info: /trade accept");
                    args.Player.SendInfoMessage("Info: /trade list");
                    args.Player.SendInfoMessage("Info: /trade collect");
                    args.Player.SendInfoMessage("Info: /trade check");
                }
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.ToString());
            }
        }

        private void AddTrade(TSPlayer player, Item item, int amount, Item witem = null, int wamount = 0)
        {
            //add to trade
            if (witem == null)
            {
                TradeList.addTrade(player.Name, item.netID, amount);
            }
            else
            {
                TradeList.addTrade(player.Name, item.netID, amount, witem.netID, wamount);
            }            
        }

        private void shopreload(CommandArgs args)
        {
            SetupConfig();
            return;
        }

        private void shop(CommandArgs args)
        {
            //main args switch
            string Switch = args.Parameters[0].ToLower();
            if (Switch == "buy")
            {
                if (!args.Player.Group.HasPermission("store.shop.buy"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to buy items!");
                    return;
                }
                //display help as no item specificed | or too many params
                if (args.Parameters.Count == 1 || args.Parameters.Count >= 4)
                {
                    args.Player.SendInfoMessage("Info: /shop {buy} {item} [amount]");
                    return;
                }
                //purchase without stack specified
                if (args.Parameters.Count == 2)
                {
                    BuyItem(args.Player, args.Parameters[1]);
                }
                //purchase with stack specified
                if (args.Parameters.Count == 3)
                {
                    BuyItem(args.Player, args.Parameters[1], Convert.ToInt32(args.Parameters[2]));
                }
            }
        }

        void BuyItem(TSPlayer player, string itemNameOrId, int stack = 1)
        {
            //Check if you can purchase that many stacks of that item in the first place...
            Item item = getItem(player, itemNameOrId, stack);
            //if null then exit
            if (item == null)
            {
                return;
            }

            //find item value
            int index = ShopList.name.IndexOf(item.name);            
            int cost = ShopList.price[index];

            //check if onsale if yes lower cost amount
            if (ShopList.onsale[index] != "")
            {
                foreach (string str in ShopList.onsale[index].Split(new char[] { ',' }))
                {
                    switch (str.ToLower())
                    {
                        case "bloodmoon":
                            cost = (int)(cost * ((100 - configObj.bloodmoon)/100));
                            break;
                        case "eclipse":
                            cost = (int)(cost * ((100 - configObj.eclipse) / 100));
                            break;
                        case "night":
                            cost = (int)(cost * ((100 - configObj.night)/100));
                            break;
                        case "day":
                            cost = (int)(cost * ((100 - configObj.day)/100));
                            break;
                    }
                }
            }

            //Check if in stock
            if (ShopList.stock[index] == 0)
            {
                player.SendErrorMessage("Error: No current stock for {0}", item.name);
                return;
            }

            //Check if has locked down group permissions
            if (!groupAllowed(player, index))
            {
                player.SendErrorMessage("Error: You do not have permissions to purchase {0}", item.name);
                return;
            }

            //Check if player has enough money to purchase the item
            var account = Wolfje.Plugins.SEconomy.SEconomyPlugin.GetEconomyPlayerSafe(player.Index);
            //Make sure balance of user is greater then group cost
            if (account.BankAccount.Balance < cost)
            {
                player.SendErrorMessage("Error: You need {1} {0} to purchase item(s)", Wolfje.Plugins.SEconomy.SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName, cost);
                return;
            }

            if (!inRegion(player, index))
            {
                player.SendErrorMessage("Error: You are currently not in range of the shop");
                return;
            }

            //Check if player has free inventory
            if (freeSlots(player, item, stack))
            {
                //All checks completed
                //Remove money and place in worldaccount
                Wolfje.Plugins.SEconomy.SEconomyPlugin.WorldAccount.TransferToAsync(account.BankAccount, -cost, Wolfje.Plugins.SEconomy.Journal.BankAccountTransferOptions.IsPayment, string.Format("Successfully Paid: {0}", cost), string.Format("Shop: {0} purhcase {1} stack of {2}", player.Name, stack, item.name));
            }
            if (ShopList.stock[index] != -1)
            {
                ShopList.stock[index] -= 1;
            }
        }

        private Boolean freeSlots(TSPlayer player, Item item, int itemAmount)
        {
            if (player.InventorySlotAvailable || (item.name.Contains("Coin") && item.type != 905) || item.type == 58 || item.type == 184)
            {
                if (player.GiveItemCheck(item.type, item.name, item.width, item.height, itemAmount))
                {
                    return true;
                }
                return false;
            }
            else
            {
                player.SendErrorMessage("Your inventory seems full.");
                return false;
            }
        }

        private Boolean inRegion(TSPlayer player, int index)
        {
            if (ShopList.region[index] != null)
            {
                foreach (Region region in ShopList.region[index])
                {
                    if (region.InArea(new Rectangle((int)(player.TileX), (int)(player.TileY), player.TPlayer.width, player.TPlayer.height)))
                    {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        private Boolean groupAllowed(TSPlayer player, int index)
        {
            if (ShopList.group[index] != null)
            {
                var cur = player.Group;
                var traversed = new List<Group>();
                var allowedGroups = ShopList.group[index];
                while (cur != null)
                {
                    if (allowedGroups.Contains(cur))
                    {
                        return true;
                    }
                    if (traversed.Contains(cur))
                    {
                        throw new InvalidOperationException("Infinite group parenting ({0})".SFormat(cur.Name));
                    }
                    traversed.Add(cur);
                    cur = cur.Parent;
                }
                return false;
            }
            return true;
        }

        private Item getItem(TSPlayer player, string itemNameOrId, int stack)
        {
            Item item = new Item();
            List<Item> matchedItems = TShock.Utils.GetItemByIdOrName(itemNameOrId);
            if (matchedItems.Count == 0)
            {
                player.SendErrorMessage("Error: Invalid item type!");
                return null;
            }
            else if (matchedItems.Count > 1)
            {
                TShock.Utils.SendMultipleMatchError(player, matchedItems.Select(i => i.name));
                return null;
            }
            else
            {
                item = matchedItems[0];
            }
            if (stack > item.maxStack)
            {
                player.SendErrorMessage("Error: Stacks entered is greater then maximum stack size");
                return null;
            }
            //all checks passed return true;
            return item;
        }

        private void SetupConfig()
        {
            try
            {
                if (File.Exists(filepath))
                {
                    configObj = new ShopConfig();
                    configObj = ShopConfig.Read(filepath);
                    return;
                }
                else
                {
                    Log.ConsoleError("Shop config not found. Creating new one");
                    configObj.Write(filepath);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.ConsoleError(ex.Message);
                return;
            }
        }
    }
}