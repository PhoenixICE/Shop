using System;
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
    [ApiVersion(1, 15)]
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
            get { return new Version(1, 2, 6); }
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
                new SqlColumn("price", MySqlDbType.Int32) { DefaultValue = "1", NotNull = true },
                new SqlColumn("region", MySqlDbType.VarChar) { DefaultValue = "", Length = 30, NotNull = true },
                new SqlColumn("groupname", MySqlDbType.VarChar) { DefaultValue = "", Length = 30, NotNull = true },
                new SqlColumn("restockTimer", MySqlDbType.Int32) { DefaultValue = "-1", NotNull = true },
                new SqlColumn("stock", MySqlDbType.Int32) { DefaultValue = "-1", NotNull = true },
                new SqlColumn("onsale", MySqlDbType.VarChar) { DefaultValue = "", Length = 30, NotNull = true },
                new SqlColumn("maxstock", MySqlDbType.Int32) { DefaultValue = "-1", Length = 30, NotNull = true }
                ));
            sqlcreator.EnsureExists(new SqlTable("storetrade",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true},
                new SqlColumn("User", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("ItemID", MySqlDbType.Int32),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("WItemID", MySqlDbType.Int32),
                new SqlColumn("WStack", MySqlDbType.Int32),
                new SqlColumn("Active", MySqlDbType.Int32)
                ));
            sqlcreator.EnsureExists(new SqlTable("storeoffer",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true },
                new SqlColumn("User", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("ItemID", MySqlDbType.Int32),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("TradeID", MySqlDbType.Int32),
                new SqlColumn("Active", MySqlDbType.Int32)
                ));

            ShopList = new ShopData(this);
            TradeList = new TradeData(this);
            configObj = new ShopConfig();
            SetupConfig();
            Commands.ChatCommands.Add(new Command("store.shop", shop, "shop"));
            Commands.ChatCommands.Add(new Command("store.admin", shopreload, "reloadstore"));
            Commands.ChatCommands.Add(new Command("store.trade", trade, "trade"));
            Commands.ChatCommands.Add(new Command("store.offer", offer, "offer"));
        }
        //offer switch
        private void offer(CommandArgs args)
        {
            //list help
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("Info: Type the below commands for more info");
                args.Player.SendInfoMessage("Info: /offer add");
                args.Player.SendInfoMessage("Info: /offer accept");
                args.Player.SendInfoMessage("Info: /offer list");
                return;
            }
            string Switch = args.Parameters[0].ToLower();
            if (Switch == "add")
            {
                if (args.Player.Group.HasPermission(""))
                if (args.Parameters.Count != 4)
                {
                    args.Player.SendInfoMessage("Info: /offer add (id) (item) (stack)");
                    args.Player.SendInfoMessage("Info: use /trade list, to find lists of IDs to offer on");
                    return;
                }
                //offering with minimum amount of args
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
                    if (item == null)
                    {
                        return;
                    }
                    for (int i = 0; i < 48; i++)
                    {
                        if (args.TPlayer.inventory[i].netID == item.netID)
                        {
                            if (args.TPlayer.inventory[i].stack >= stack)
                            {
                                //all conditions met, delete item and add offer entry.   
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                }
                                else
                                {
                                    args.TPlayer.inventory[i].stack -= stack;
                                }
                                TradeList.processOffer(args.Player, id, item.netID, stack);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                args.Player.SendInfoMessage("Sucess: Offer Completed Successfully! You have offered {0} of {1} on TradeID {2}.", stack, item.name, id);
                                return;
                            }
                        }
                    }
                    args.Player.SendErrorMessage("Error: Adding Trade could not be completed, you do not have that item to Trade, or do not have enough stacks in one pile!");
                    args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                    return;
                }
            }
            //offer accept (id)
            else if (Switch == "accept")
            {
                if (args.Parameters.Count != 2)
                {
                    args.Player.SendInfoMessage("Info: /offer accept (id)");
                    args.Player.SendInfoMessage("Info: use /trade list, to find lists of IDs to offer on");
                    return;
                }
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
                if (oObj.Type == -1 || oObj.Active == 0)
                {
                    args.Player.SendErrorMessage("Error: You cannot accept this Offer");
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
                        foreach (OfferObj obj in TradeList.offerObj)
                        {
                            if (obj.Type == tradeID && obj.Active == 1)
                            {
                                TradeList.returnOffer(obj);
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
            //offer list
            if (Switch == "list")
            {
                if (args.Parameters.Count != 2)
                {
                    args.Player.SendInfoMessage("Info: /offer list (id)");
                    args.Player.SendInfoMessage("Info: Lists all offers for a trade ID");
                    return;
                }
                if (args.Parameters.Count == 2)
                {
                    int id;
                    if (!int.TryParse(args.Parameters[1], out id))
                    {
                        args.Player.SendErrorMessage("Error: Invalid ID Entered!");
                        return;
                    }
                    TradeObj tObj = TradeList.TradeObjByID(id);
                    if ((tObj.User != args.Player.Name || !args.Player.Group.HasPermission("store.admin")) && tObj.Active == 1)
                    {
                        args.Player.SendErrorMessage("Error: You cannot check offers on this item!");
                        return;
                    }
                    args.Player.SendMessage("ID - User - Offered Item:Stack", Color.Green);
                    foreach (OfferObj obj in TradeList.offerObj)
                    {
                        if (obj.Type == id && obj.Active == 1)
                            args.Player.SendInfoMessage("{0} - {1} - {2}:{3}", obj.ID, obj.User, TShock.Utils.GetItemById(obj.ItemID).name, obj.Stack);
                    }
                    return;
                }
            }
            else if (Switch == "cancel")
            {
                if (!args.Player.Group.HasPermission("store.offer.cancel"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to cancel offers!");
                    return;
                }
                if (args.Parameters.Count != 2)
                {
                    args.Player.SendInfoMessage("Info: /offer cancel (id)");
                    args.Player.SendInfoMessage("Info: Cancels all offers you made for a trade id");
                    return;
                }
                int id;
                if (!int.TryParse(args.Parameters[1], out id))
                {
                    args.Player.SendErrorMessage("Error: Inccorect ID entered!");
                    return;
                }
                TradeObj obj = TradeList.TradeObjByID(id);
                if (obj == null)
                {
                    args.Player.SendErrorMessage("Error: Inccorect ID entered!");
                    return;
                }
                //all checks complete send trade back to collection queue
                TradeList.cancelOffers(args.Player.Name);
                args.Player.SendInfoMessage("Info: Offers have been cancelled, please use /trade collect to reclaim your items.");
                return;
            }
            else
            {
                args.Player.SendErrorMessage("Error: Incorrect Switch used - {0}", args.Parameters[0]);
                return;
            }
        }
        //trade switch
        private void trade(CommandArgs args)
        {
            //list help
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage("Info: Type the below commands for more info");
                args.Player.SendInfoMessage("Info: /trade add");
                args.Player.SendInfoMessage("Info: /trade accept");
                args.Player.SendInfoMessage("Info: /trade list");
                args.Player.SendInfoMessage("Info: /trade collect");
                args.Player.SendInfoMessage("Info: /trade check");
                args.Player.SendInfoMessage("Info: /trade search");
                args.Player.SendInfoMessage("Info: /trade cancel");
                return;
            }
            //main args switch
            string Switch = args.Parameters[0].ToLower();
            //trade add (item) (amount) (currency) - 1 to currency trade
            //trade add (item) (amount) - 1 to offer trade
            //trade add (item) (amount) (witem) (wamount) - 1 to 1 trade
            if (Switch == "add")
            {
                if (!args.Player.Group.HasPermission("store.trade.add"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to add trades!");
                    return;
                }
                //display help as no item specificed | or too many params
                if (args.Parameters.Count == 1 || args.Parameters.Count >= 6)
                {
                    args.Player.SendInfoMessage("Info: /trade add (item) (amount) [witem] [wamount]");
                    args.Player.SendInfoMessage("Info: Set the item you wish to trade and the amount of them, optionally set the item you wish to trade for and the amount of them");
                    args.Player.SendInfoMessage("Info: /trade add (item) (amount) ({0})", SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName);
                    args.Player.SendInfoMessage("Info: Set the item you wish to trade, the amount of them and the amount of {0} you wish to exchange for.", SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName);
                    return;
                }
                //Trading with minimum required amount of args
                //trade add (item) (amount) - 1 to offer trade
                if (args.Parameters.Count() == 3)
                {
                    if (!args.Player.Group.HasPermission("store.trade.add.offer"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to add trades for offers!");
                        return;
                    }
                    int stack;
                    if (!int.TryParse(args.Parameters[2], out stack))
                    {
                        args.Player.SendErrorMessage("Error: Stack size must be numeric!");
                        args.Player.SendErrorMessage("Error: You have entered: {0}", args.Parameters[2]);
                        return;
                    }
                    if (stack <= 0)
                    {
                        args.Player.SendErrorMessage("Error: Can't have stack size of zero or lower!");
                        return;
                    }
                    Item item = getItem(args.Player, args.Parameters[1], 1);
                    if (item == null)
                    {
                        return;
                    }
                    for (int i = 0; i < 48; i++)
                    {
                        if (args.TPlayer.inventory[i].netID == item.netID)
                        {
                            if (args.TPlayer.inventory[i].stack >= stack)
                            {
                                //all conditions met, delete item and add trade entry.
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                }
                                else
                                {
                                    args.TPlayer.inventory[i].stack -= stack;
                                }
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                AddTrade(args.Player, item, stack);
                                args.Player.SendInfoMessage("Sucess: Add Trade Successfully! You have Added {0} {1}(s).", stack, item.name);
                                return;
                            }
                        }
                    }
                    args.Player.SendErrorMessage("Error: Adding Trade could not be completed, you do not have that item to Trade, or do not have enough stacks in one pile!");
                    args.Player.SendErrorMessage("Error: Trade Item - {0}", item.name);
                    return;
                }
                //trade add (item) (amount) (currency) - 1 to currency trade
                else if (args.Parameters.Count() == 4)
                {
                    if (!args.Player.Group.HasPermission("store.trade.add.currency"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to add trades for {0}!", SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName);
                        return;
                    }
                    int stack;
                    if (!int.TryParse(args.Parameters[2], out stack))
                    {
                        args.Player.SendErrorMessage("Error: Invalid stack size!");
                        args.Player.SendErrorMessage("Error: You have entered {0}", args.Parameters[2]);
                        return;
                    }
                    //check for negative
                    if (stack <= 0)
                    {
                        args.Player.SendErrorMessage("Error: Can't have stack size of zero or lower!");
                        return;
                    }
                    int money;
                    if (!int.TryParse(args.Parameters[3], out money))
                    {
                        args.Player.SendErrorMessage("Error: Invalid Currency value!");
                        args.Player.SendErrorMessage("Error: You have entered {0}", args.Parameters[3]);
                        return;
                    }
                    if (money <= 0)
                    {
                        args.Player.SendErrorMessage("Error: Can't have currency value of zero or lower!");
                        return;
                    }
                    //check item
                    Item item = getItem(args.Player, args.Parameters[1], stack);
                    if (item == null)
                    {
                        return;
                    }
                    //check if user has the item
                    for (int i = 0; i < 48; i++)
                    {
                        if (args.TPlayer.inventory[i].netID == item.netID)
                        {
                            if (args.TPlayer.inventory[i].stack >= stack)
                            {
                                //all conditions met, delete item and add offer entry.
                                TradeList.addExchange(args.Player, item, stack, money);
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                }
                                else
                                {
                                    args.TPlayer.inventory[i].stack -= stack;
                                }
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                args.Player.SendInfoMessage("Sucess: Adding Trade Completed Successfully! You are Trading {0} of {1} for {2}.", stack, item.name, ((Money)money).ToLongString());
                                return;
                            }
                        }
                    }
                    args.Player.SendErrorMessage("Error: Offer could not be completed, you do not have that item to offer, or not enough stacks in one pile!");
                    args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                    return;        
                }
                //Trading with maximum required amount of args
                //trade add (item) (amount) (witem) (wamount) - 1 to 1 trade
                else if (args.Parameters.Count() == 5)
                {
                    if (!args.Player.Group.HasPermission("store.trade.add.item"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to add trades for other items!");
                        return;
                    }
                    int stack;
                    if (!int.TryParse(args.Parameters[2], out stack))
                    {
                        args.Player.SendErrorMessage("Error: Stack size must be numeric!");
                        args.Player.SendErrorMessage("Error: You have entered: {0}", args.Parameters[2]);
                    }
                    if (stack <= 0)
                    {
                        args.Player.SendErrorMessage("Error: Can't have stack size of zero or less!");
                        return;
                    }
                    Item item = getItem(args.Player, args.Parameters[1], 1);
                    if (item == null)
                    {
                        return;
                    }
                    int wstack;
                    if (!Int32.TryParse(args.Parameters[4], out wstack))
                    {
                        args.Player.SendErrorMessage("Error: Wanted stack size must be numeric!");
                        args.Player.SendErrorMessage("Error: You have entered: {0}", args.Parameters[4]);
                    }
                    if (wstack <= 0)
                    {
                        args.Player.SendErrorMessage("Error: Can't have wanted stack size of zero or less");
                        return;
                    }
                    Item witem = getItem(args.Player, args.Parameters[3], 1);
                    if (witem == null)
                    {
                        return;
                    }
                    for (int i = 0; i < 48; i++)
                    {
                        if (args.TPlayer.inventory[i].netID == item.netID)
                        {
                            if (args.TPlayer.inventory[i].stack >= stack)
                            {
                                //all conditions met, delete item and add offer entry.   
                                if (args.TPlayer.inventory[i].stack == stack)
                                {
                                    args.TPlayer.inventory[i].SetDefaults(0);
                                }
                                else
                                {
                                    args.TPlayer.inventory[i].stack -= stack;
                                }
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                AddTrade(args.Player, item, stack, witem, wstack);
                                args.Player.SendInfoMessage("Sucess: Add Trade Successfully! You have Added {0} {1}(s).", stack, item.name);
                                args.Player.SendInfoMessage("Sucess: Trading for {0} {1}(s).", wstack, witem.name);
                                return;
                            }
                        }
                    }
                    args.Player.SendErrorMessage("Error: Offer could not be completed, you do not have that item to offer, or not enough stacks in one pile!");
                    args.Player.SendErrorMessage("Error: Offered Item - {0}", item.name);
                    return;
                }
            }
            else if (Switch == "list")
            {
                if (!args.Player.Group.HasPermission("store.trade.list"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to list trades!");
                    return;
                }
                //List first 9 (This is NOT WORKING, needs revision, as of now it ignores page size and lists whole list of items)
                if (args.Parameters.Count <= 2)
                {
                    int firstpage = 1;
                    int lastpage = 9;
                    if (args.Parameters.Count == 2)
                    {
                        if (int.TryParse(args.Parameters[1], out lastpage))
                        {
                            lastpage *= 9;
                            firstpage = lastpage - 8;
                        }
                    }

                    args.Player.SendMessage("ID - User - Item:Stack - Wanted:Stack", Color.Green);
                    int sent = 0;
                    int check = 0;
                    foreach (TradeObj obj in TradeList.tradeObj)
                    {
                        if (obj.Active == 1 && check >= firstpage)
                        {
                            check++;
                            string item = "";
                            string witem = "";
                            if (obj.ItemID == 0)
                            {
                                item = SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName; ;
                            }
                            else
                            {
                                item = TShock.Utils.GetItemById(obj.ItemID).name;
                            }

                            if (obj.WItemID == 0)
                            {
                                witem = SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName;

                            }
                            else
                            {
                                witem = TShock.Utils.GetItemById(obj.WItemID).name;
                            }
                            args.Player.SendInfoMessage("{0} - {1} - {2}:{3} - {4}:{5}", obj.ID, obj.User, item, obj.Stack, witem, obj.WStack);
                            sent++;
                        }
                        if (sent == lastpage)
                        {
                            break;
                        }
                    }
                    if (check < TradeList.tradeObj.Count())
                        args.Player.SendInfoMessage("/trade list {0}", (check / 9) + 1);
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
                //display help as no item specificed or too many params
                if (args.Parameters.Count == 1 || args.Parameters.Count > 2)
                {
                    args.Player.SendInfoMessage("Info: /trade accept (id)");
                    args.Player.SendInfoMessage("Info: use /trade list, to accept lists of trades");
                    return;
                }
                //Accepting with required amount of args
                if (args.Parameters.Count == 2)
                {
                    int id;
                    if (!int.TryParse(args.Parameters[1], out id))
                    {
                        args.Player.SendErrorMessage("Error: Inccorect ID entered!");
                        return;
                    }
                    TradeObj obj = TradeList.TradeObjByID(id);
                    if (obj == null || obj.Active == 0)
                    {
                        args.Player.SendErrorMessage("Error: Could not locate the Trade ID!");
                        return;
                    }
                    //check if wanted item is listed, otherwise exit
                    if (obj.WItemID == 0 && obj.WStack == 0)
                    {
                        args.Player.SendErrorMessage("Error: Cannot accept trade as no wanted item is listed.");
                        args.Player.SendErrorMessage("Error: Please use /offer add, to make an offer to the player.");
                        return;
                    }
                    Item witem = new Item();
                    if (obj.WItemID != 0)
                    {
                        witem = TShock.Utils.GetItemById(obj.WItemID);
                    }
                    else
                    {
                        witem = null;
                    }
                    int wstack = obj.WStack;
                    if (witem != null)
                    {
                        //check if player actually has the item requested
                        for (int i = 0; i < 48; i++)
                        {
                            if (args.TPlayer.inventory[i].netID == witem.netID)
                            {
                                if (args.TPlayer.inventory[i].stack >= wstack)
                                {
                                    //remove trade and add completed offer for the witem
                                    TradeList.processTrade(args.Player, obj, witem, wstack);
                                    //all conditions met, delete witem and replace with item as the prize                                   
                                    args.TPlayer.inventory[i].SetDefaults(obj.ItemID);
                                    args.TPlayer.inventory[i].stack = obj.Stack;
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    args.Player.SendInfoMessage("Sucess: Trade Completed Successfully! You have gained {0} of {1} for {2} of {3}.", obj.Stack, TShock.Utils.GetItemById(obj.ItemID).name, obj.WStack, TShock.Utils.GetItemById(obj.WItemID).name);
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
                    //currency trade
                    else
                    {
                        //get users account
                        EconomyPlayer eaccount = SEconomyPlugin.GetEconomyPlayerByBankAccountNameSafe(args.Player.Name);
                        //check if user actually has that much to pay
                        if (eaccount.BankAccount.Balance >= wstack)
                        {
                            bool recived = false;
                            for (int i = 0; i < 48; i++)
                            {
                                if (args.TPlayer.inventory[i].netID == 0)
                                {
                                    //all conditions met, add item and add offer entry.  
                                    TradeList.processTrade(args.Player, obj, witem, wstack);
                                    args.TPlayer.inventory[i].SetDefaults(obj.ItemID);
                                    args.TPlayer.inventory[i].stack = obj.Stack;
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                    NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                    args.Player.SendInfoMessage("Sucess: Item Collected! You have Gained {0} of {1}.", obj.Stack, TShock.Utils.GetItemById(obj.ItemID).name);
                                    recived = true;
                                    eaccount.BankAccount.TransferToAsync(SEconomyPlugin.WorldAccount, wstack, BankAccountTransferOptions.IsPayment | BankAccountTransferOptions.AnnounceToSender, null, string.Format("Shop Plugin: Traded {0} for {1}", obj.ItemID, ((Money)wstack).ToLongString()));
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
                        else
                        {
                            args.Player.SendErrorMessage("Error: You do not have enough {0}", SEconomyPlugin.Configuration.MoneyConfiguration.MoneyName);
                            args.Player.SendErrorMessage("Error: Required amount is {0}", ((Money)wstack).ToLongString());
                            return;
                        }
                    }
                }
            }
            else if (Switch == "check")
            {
                if (args.Parameters.Count != 1)
                {
                    args.Player.SendInfoMessage("Info: /trade check");
                    args.Player.SendInfoMessage("Info: Lists your current trades");
                    return;
                }
                //List trades
                if (args.Parameters.Count == 1)
                {
                    args.Player.SendMessage("ID - User - Item:Stack - Wanted:Stack", Color.Green);
                    foreach (TradeObj obj in TradeList.tradeObj)
                    {
                        if (obj.User == args.Player.Name && obj.Active == 1)
                            args.Player.SendInfoMessage("{0} - {1} - {2}:{3} - {4}:{5}", obj.ID, obj.User, TShock.Utils.GetItemById(obj.ItemID).name, obj.Stack, TShock.Utils.GetItemById(obj.WItemID).name, obj.WStack);
                    }
                    return;
                }                    
            }
            else if (Switch == "collect")
            {
                if (args.Parameters.Count() != 1)
                {
                    args.Player.SendInfoMessage("Info: /trade collect");
                    args.Player.SendInfoMessage("Info: Collects traded items from trades, and any finished/rejected trades");
                    //args.Player.SendInfoMessage("Info: Specifying id accepts that trade deal - use /trade check to find ids");
                    return;
                }
                //accept all stuff
                if (args.Parameters.Count() == 1)
                {
                    foreach (OfferObj obj in TradeList.offerObj)
                    {
                        if (obj.User == args.Player.Name && obj.Type == -1 && obj.Active == 1)
                        {
                            //accepting actual item
                            if (obj.ItemID != 0)
                            {
                                bool recived = false;
                                for (int i = 0; i < 48; i++)
                                {
                                    if (args.TPlayer.inventory[i].netID == 0)
                                    {
                                        //all conditions met, delete item and add offer entry.  
                                        TradeList.processAccept(args.Player, obj);
                                        args.TPlayer.inventory[i].SetDefaults(obj.ItemID);
                                        args.TPlayer.inventory[i].stack = obj.Stack;
                                        NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", args.Player.Index, i);
                                        NetMessage.SendData((int)PacketTypes.PlayerSlot, args.Player.Index, -1, "", args.Player.Index, i);
                                        args.Player.SendInfoMessage("Sucess: Item Collected! You have Gained {0} {1}.", obj.Stack, TShock.Utils.GetItemById(obj.ItemID).name);
                                        recived = true;
                                        break;
                                    }
                                }
                                if (!recived)
                                {
                                    args.Player.SendErrorMessage("Error: You have no free inventory spaces!");
                                    return;
                                }
                            }
                            //accepting currency
                            else
                            {
                                TradeList.processAccept(args.Player, obj);
                                EconomyPlayer eaccount = SEconomyPlugin.GetEconomyPlayerSafe(args.Player.Index);
                                SEconomyPlugin.WorldAccount.TransferToAsync(eaccount.BankAccount, obj.Stack, BankAccountTransferOptions.IsPayment | BankAccountTransferOptions.AnnounceToReceiver, null, string.Format("Shop: Collected Offer ID {0}", obj.ID));
                            }
                        }
                    }
                    args.Player.SendInfoMessage("Info: All Items Recieved!");
                    return;                        
                }
            }
            else if (Switch == "cancel")
            {
                if (!args.Player.Group.HasPermission("store.trade.cancel"))
                {
                    args.Player.SendErrorMessage("Error: You do not have permission to cancel trades!");
                    return;
                }
                if (args.Parameters.Count != 2)
                {
                    args.Player.SendInfoMessage("Info: /trade cancel (id)");
                    args.Player.SendInfoMessage("Info: Cancels a traded for the trade list");
                    return;
                }
                int id;
                if (!int.TryParse(args.Parameters[1], out id))
                {
                    args.Player.SendErrorMessage("Error: Invalid ID entered!");
                    return;
                }
                TradeObj obj = TradeList.TradeObjByID(id);
                if (obj == null)
                {
                    args.Player.SendErrorMessage("Error: Incorrect ID entered!");
                    return;
                }
                if ((obj.User != args.Player.Name || !args.Player.Group.HasPermission("store.admin")) && obj.Active == 1)
                {
                    args.Player.SendErrorMessage("Error: You do not own this trade or trade has already been completed!");
                    return;
                }
                //all checks complete send trade back to collection queue
                TradeList.cancelTrade(obj);
                args.Player.SendInfoMessage("Info: Trade has been cancelled, please use /trade collect to reclaim your items.");
                return;
            }
            else if (Switch == "search")
            {
                if (args.Parameters.Count != 2)
                {
                    args.Player.SendInfoMessage("Info: /trade search (name)");
                    args.Player.SendInfoMessage("Info: Searches the trade list for all occurences of said item");
                    return;
                }
                List<TradeObj> objlist = TradeList.TradeObjByName(args.Parameters[1]);
                if (objlist.Count() < 1)
                {
                    args.Player.SendErrorMessage("Error: No items could be found / More than one item matched!");
                    return;
                }
                //List trades
                args.Player.SendMessage("ID - User - Item:Stack - Wanted:Stack", Color.Green);
                foreach (TradeObj obj in objlist)
                {
                    args.Player.SendInfoMessage("{0} - {1} - {2}:{3} - {4}:{5}", obj.ID, obj.User, TShock.Utils.GetItemById(obj.ItemID).name, obj.Stack, TShock.Utils.GetItemById(obj.WItemID).name, obj.WStack);
                }
                    return;            
                
            }
            else
            {
                args.Player.SendErrorMessage("Error: Incorrect Switch used - {0}", args.Parameters[0]);
                return;
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
            try
            {
                ShopList = new ShopData(this);
                SetupConfig();
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.ToString());
            }
        }
        //shop switch
        private void shop(CommandArgs args)
        {
            //main args switch
            if (args.Parameters.Count() == 0)
            {
                //Display Help
                args.Player.SendInfoMessage("Info: /shop buy (item) [amount]");
                args.Player.SendInfoMessage("Info: /shop search (item)");
                return;
            }
            try
            {
                string Switch = args.Parameters[0].ToLower();
                if (Switch == "buy")
                {
                    if (!args.Player.Group.HasPermission("store.shop.buy"))
                    {
                        args.Player.SendErrorMessage("Error: You do not have permission to buy items!");
                        return;
                    }
                    //display help as no item specificed | or too many params
                    if (args.Parameters.Count == 1 || args.Parameters.Count > 3)
                    {
                        args.Player.SendInfoMessage("Info: /shop buy (item) [amount]");
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
                        int stack;
                        if (!int.TryParse(args.Parameters[2], out stack))
                        {
                            args.Player.SendErrorMessage("Error: Invalid stack size!");
                            args.Player.SendErrorMessage("Error: You have entered {0}", args.Parameters[2]);
                            return;
                        }
                        if (stack < 1)
                        {
                            args.Player.SendErrorMessage("Error: Cannot have a stack size that is zero or less!");
                            args.Player.SendErrorMessage("Error: You have entered {0}", stack);
                            return;
                        }
                        BuyItem(args.Player, args.Parameters[1], stack);
                    }
                }
                else if(Switch == "search")
                {
                    //display help as no item specificed | or too many params
                    if (args.Parameters.Count == 1 || args.Parameters.Count > 2)
                    {
                        args.Player.SendInfoMessage("Info: /shop search (item)");
                        return;
                    }
                    Item item = getItem(args.Player, args.Parameters[1], 1);
                    if (item == null)
                    {
                        return;
                    }
                    ShopObj obj = ShopList.FindShopObjbyItemName(item.name);
                    if (obj == null)
                    {
                        args.Player.SendInfoMessage("Info: This item is not listed for sale.");
                        return;
                    }
                    string interval;
                    string stock = "";
                    string MaxStock = "";
                    if (obj.RestockTimer == null)
                    {
                        interval = "Doesn't Restock";
                    }
                    else
                    {
                        interval = ((int)obj.RestockTimer.Interval).ToString();
                    }
                    if (obj.Stock == -1)
                    {
                        stock = "Infinity";
                    }
                    else
                    {
                        stock = obj.Stock.ToString();
                    }
                    if (obj.MaxStock == -1)
                    {
                        MaxStock = "Infinity";
                    }
                    else
                    {
                        MaxStock = obj.MaxStock.ToString();
                    }

                    args.Player.SendInfoMessage("Item: {0} Price: {1} Stock: {2} MaxStock: {3} Restock Interval: {4}", obj.Item, obj.Price, stock, MaxStock, interval);
                    if (obj.Region.Count() == 0)
                    {
                        args.Player.SendInfoMessage("Shop: No Shop Restrictions");
                    }
                    else
                    {
                        string regions = "";
                        foreach (string str in obj.Region)
                        {
                            regions += str + ", ";
                        }
                        regions = regions.Remove(regions.Length - 2);
                        args.Player.SendInfoMessage("Shop: {0}", regions);
                    }
                    if (obj.Group.Count() == 0)
                    {
                        args.Player.SendInfoMessage("Group: No Group Restrictions");
                    }
                    else
                    {
                        string groups = "";
                        foreach (string str in obj.Group)
                        {
                            groups += str + ", ";
                        }
                        groups = groups.Remove(groups.Length - 2);
                        args.Player.SendInfoMessage("Group: {0}", groups);
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Error: Incorrect Switch used - {0}", args.Parameters[0]);
                    return;     
                }
            }
            catch (Exception e)
            {
                Log.ConsoleError(e.ToString());
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
            ShopObj obj = ShopList.FindShopObjbyItemName(item.name);
            if (obj == null)
            {
                player.SendErrorMessage("Error: This item cannot be Purchased! - {0}", item.name);
                return;
            }

            //Check if in stock
            if (obj.Stock == 0)
            {
                player.SendErrorMessage("Error: No current stock for {0}", item.name);
                return;
            }

            //Check if there is enough stock
            if (obj.Stock >= stack)
            {
                player.SendErrorMessage("Error: Not enough stock left for {0} to purchase {1}", item.name, stack);
                return;
            }

            //Check if has locked down group permissions
            if (!groupAllowed(player, obj.Group))
            {
                player.SendErrorMessage("Error: You do not have permissions to purchase {0}", item.name);
                return;
            }

            //Check if within region
            if (!inRegion(player, obj.Region))
            {
                player.SendErrorMessage("Error: You are currently not in range of the shop");
                return;
            }

            int cost = obj.Price * stack;

            //check if onsale if yes lower cost amount
            if (obj.Onsale.Count != 0)
            {
                foreach (string str in obj.Onsale)
                {
                    switch (str.ToLower())
                    {
                        case "bloodmoon":
                            if (Main.bloodMoon)
                                cost = (int)(cost * ((100 - configObj.bloodmoon)/100));
                            break;
                        case "eclipse":
                            if (Main.eclipse)
                                cost = (int)(cost * ((100 - configObj.eclipse) / 100));
                            break;
                        case "night":
                            if (!Main.dayTime)
                                cost = (int)(cost * ((100 - configObj.night)/100));
                            break;
                        case "day":
                            if (Main.dayTime)
                                cost = (int)(cost * ((100 - configObj.day)/100));
                            break;
                    }
                }
            }

            //Check if player has enough money to purchase the item
            EconomyPlayer account = Wolfje.Plugins.SEconomy.SEconomyPlugin.GetEconomyPlayerSafe(player.Index);
            //Make sure balance of user is greater then group cost
            if (account.BankAccount.Balance < cost)
            {
                player.SendErrorMessage("Error: You do not have enough to Purchase this item!");
                player.SendErrorMessage("Required: {0}!", ((Money)cost).ToLongString());
                return;
            }

            //Check if player has free inventory
            if (freeSlots(player, item, stack))
            {
                //All checks completed
                //Remove money and place in worldaccount
                account.BankAccount.TransferToAsync(SEconomyPlugin.WorldAccount, cost, Wolfje.Plugins.SEconomy.Journal.BankAccountTransferOptions.IsPayment, obj.Item, string.Format("Shop: {0} purhcase {1} stack of {2}", player.Name, stack, item.name));
            }
            else
            {
                return;
            }
            ShopList.lowerStock(obj, stack);
        }

        private Boolean freeSlots(TSPlayer player, Item item, int itemAmount)
        {
            if (player.InventorySlotAvailable || (item.name.Contains("Coin") && item.type != 905) || item.type == 58 || item.type == 184)
            {
                if (player.GiveItemCheck(item.type, item.name, item.width, item.height, itemAmount))
                {
                    return true;
                }
                player.SendErrorMessage("Error: An unknown error has occured - this code should not be reachable!");
                return false;
            }
            else
            {
                player.SendErrorMessage("Error: Your inventory seems to be full!");
                return false;
            }
        }

        private Boolean inRegion(TSPlayer player, List<string> regions)
        {
            if (regions.Count() != 0)
            {
                foreach (string region in regions)
                {
                    try
                    {
                        if (TShock.Regions.GetRegionByName(region).InArea(new Rectangle((int)(player.TileX), (int)(player.TileY), player.TPlayer.width, player.TPlayer.height)))
                        {
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        Log.ConsoleError("Shop Plugin: Error cannot locate region - {0}", region);
                        return false;
                    }
                }
            }
            else
            {
                return true;
            }
            return false;
        }

        private Boolean groupAllowed(TSPlayer player, List<string> groups)
        {
            if (groups.Count() != 0)
            {                
                var cur = player.Group;
                foreach (string group in groups)
                {
                    try
                    {
                        if (player.Group.HasPermission(TShock.Groups.GetGroupByName(group).Name))
                        {
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        Log.ConsoleError("Shop Plugin: Error cannot locate group - {0}", group);
                        return false;
                    }
                }
            }
            else
            {
                return true;
            }
            return false;
        }

        private Item getItem(TSPlayer player, string itemNameOrId, int stack)
        {
            Item item = new Item();
            List<Item> matchedItems = TShock.Utils.GetItemByIdOrName(itemNameOrId);
            if (matchedItems == null || matchedItems.Count == 0)
            {
                player.SendErrorMessage("Error: Incorrect item name or ID, please use quotes if the item has a space in it!");
                player.SendErrorMessage("Error: You have entered: {0}", itemNameOrId);
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