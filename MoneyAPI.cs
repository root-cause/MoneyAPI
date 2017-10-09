using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using GrandTheftMultiplayer.Server.API;
using GrandTheftMultiplayer.Server.Elements;

namespace MoneyAPI
{
    public class MoneyAPI : Script
    {
        /* Config - don't touch these, modify meta.xml instead */
        string DBFile = "wallets.db";
        long StartingMoney = 250;
        long MoneyCap = 9000000000000000;
        bool SaveEverytime = false;
        int AutosaveInterval = 5;
        bool Logging = false;

        HashSet<Client> SaveList = new HashSet<Client>();
        DateTime LastAutosave = DateTime.Now;

        public string GetDBPath()
        {
            return API.getResourceFolder() + Path.DirectorySeparatorChar + DBFile;
        }

        /* spaghetti begins here */
        public long Clamp(long value, long min, long max) // http://stackoverflow.com/a/3176617
        {
            return (value <= min) ? min : (value >= max) ? max : value;
        }

        public void InitPlayer(Client player)
        {
            using (SQLiteConnection sqlCon = new SQLiteConnection( string.Format("Data Source={0}", GetDBPath()) ))
            {
                using (SQLiteCommand sqlCmd = new SQLiteCommand(sqlCon))
                {
                    try {
                        sqlCon.Open();

                        sqlCmd.CommandText = "SELECT Money FROM wallets WHERE SocialClub=@sc";
                        sqlCmd.Parameters.AddWithValue("@sc", player.socialClubName);

                        using (SQLiteDataReader sqlRdr = sqlCmd.ExecuteReader())
                        {
                            if (sqlRdr.HasRows) {
                                sqlRdr.Read();
                                long money = sqlRdr.GetInt64(0);

                                player.setData("wallet_Amount", money);
                                API.triggerClientEvent(player, "UpdateMoneyHUD", Convert.ToString(money)); // disgusting hack for long support
                            } else {
                                CreateWallet(player);
                            }
                        }

                        sqlCon.Close();
                    } catch (SQLiteException e) {
                        API.consoleOutput("InitPlayer Error: #{0} - {1}", e.ErrorCode, e.Message);
                    }
                }
            }
        }

        public void CreateWallet(Client player)
        {
            using (SQLiteConnection sqlCon = new SQLiteConnection( string.Format("Data Source={0}", GetDBPath()) ))
            {
                using (SQLiteCommand sqlCmd = new SQLiteCommand(sqlCon))
                {
                    try {
                        sqlCon.Open();

                        sqlCmd.CommandText = "INSERT INTO wallets (SocialClub, Money) VALUES (@sc, @money)";
                        sqlCmd.Parameters.AddWithValue("@sc", player.socialClubName);
                        sqlCmd.Parameters.AddWithValue("@money", StartingMoney);
                        sqlCmd.ExecuteNonQuery();

                        sqlCon.Close();

                        player.setData("wallet_Amount", StartingMoney);
                        API.triggerClientEvent(player, "UpdateMoneyHUD", Convert.ToString(StartingMoney)); // disgusting hack for long support
                    } catch (SQLiteException e) {
                        API.consoleOutput("CreateWallet Error: #{0} - {1}", e.ErrorCode, e.Message);
                    }
                }
            }
        }

        public bool SaveWallet(Client player, bool inside_loop = false)
        {
            if (!player.hasData("wallet_Amount")) return false;

            using (SQLiteConnection sqlCon = new SQLiteConnection( string.Format("Data Source={0}", GetDBPath()) ))
            {
                using (SQLiteCommand sqlCmd = new SQLiteCommand(sqlCon))
                {
                    try {
                        sqlCon.Open();

                        sqlCmd.CommandText = "UPDATE wallets SET Money=@money WHERE SocialClub=@sc";
                        sqlCmd.Parameters.AddWithValue("@money", player.getData("wallet_Amount"));
                        sqlCmd.Parameters.AddWithValue("@sc", player.socialClubName);
                        sqlCmd.ExecuteNonQuery();

                        sqlCon.Close();
                        if (!inside_loop) SaveList.Remove(player);
                        return true;
                    } catch (SQLiteException e) {
                        API.consoleOutput("SaveWallet Error: #{0} - {1}", e.ErrorCode, e.Message);
                        return false;
                    }
                }
            }
        }

        public void WalletLog(Client player, long amount, string function)
        {
            using (SQLiteConnection sqlCon = new SQLiteConnection( string.Format("Data Source={0}", GetDBPath()) ))
            {
                using (SQLiteCommand sqlCmd = new SQLiteCommand(sqlCon))
                {
                    try {
                        sqlCon.Open();

                        sqlCmd.CommandText = "INSERT INTO wallet_logs (SocialClub, Amount, Function, Date) VALUES (@sc, @amount, @func, DATETIME('NOW', 'LOCALTIME'))";
                        sqlCmd.Parameters.AddWithValue("@sc", player.socialClubName);
                        sqlCmd.Parameters.AddWithValue("@amount", amount);
                        sqlCmd.Parameters.AddWithValue("@func", function);
                        sqlCmd.ExecuteNonQuery();

                        sqlCon.Close();
                    } catch (SQLiteException e) {
                        API.consoleOutput("{0} Logging Error: #{1} - {2}", function, e.ErrorCode, e.Message);
                    }
                }
            }
        }

        public long GetMoney(Client player)
        {
            return (player.hasData("wallet_Amount")) ? player.getData("wallet_Amount") : 0;
        }

        public void ChangeMoney(Client player, long amount)
        {
            if (!player.hasData("wallet_Amount")) return;
            player.setData("wallet_Amount", Clamp(player.getData("wallet_Amount") + amount, MoneyCap * -1, MoneyCap));
            API.triggerClientEvent(player, "UpdateMoneyHUD", Convert.ToString(player.getData("wallet_Amount")), Convert.ToString(amount)); // disgusting hack for long support
            SaveList.Add(player);

            if (SaveEverytime) SaveWallet(player);
            if (Logging) WalletLog(player, amount, System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

        public void SetMoney(Client player, long amount)
        {
            if (!player.hasData("wallet_Amount")) return;
            player.setData("wallet_Amount", Clamp(amount, MoneyCap * -1, MoneyCap));
            API.triggerClientEvent(player, "UpdateMoneyHUD", Convert.ToString(player.getData("wallet_Amount"))); // disgusting hack for long support
            SaveList.Add(player);

            if (SaveEverytime) SaveWallet(player);
            if (Logging) WalletLog(player, amount, System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

        public MoneyAPI()
        {
            API.onResourceStart += MoneyAPI_Init;
            API.onResourceStop += MoneyAPI_Exit;
            API.onUpdate += MoneyAPI_Update;
            API.onPlayerFinishedDownload += MoneyAPI_PlayerJoin;
            API.onPlayerDisconnected += MoneyAPI_PlayerLeave;
        }

        public void MoneyAPI_Init()
        {
            if (API.hasSetting("walletDB")) DBFile = API.getSetting<string>("walletDB");
            if (API.hasSetting("walletDefault")) StartingMoney = API.getSetting<long>("walletDefault");
            if (API.hasSetting("walletCap")) MoneyCap = API.getSetting<long>("walletCap");
            if (API.hasSetting("walletInterval")) AutosaveInterval = API.getSetting<int>("walletInterval");
            if (API.hasSetting("walletSave")) SaveEverytime = API.getSetting<bool>("walletSave");
            if (API.hasSetting("walletLog")) Logging = API.getSetting<bool>("walletLog");

            API.consoleOutput("MoneyAPI Loaded");
            API.consoleOutput("-> Database: {0}", DBFile);
            API.consoleOutput("-> Starting Money: ${0:n0}", StartingMoney);
            API.consoleOutput("-> Money Cap: ${0:n0}", MoneyCap);
            API.consoleOutput("-> Autosave: {0}", AutosaveInterval == 0 ? "Disabled" : "every " + AutosaveInterval + " minutes");
            API.consoleOutput("-> Save After Operation: {0}", SaveEverytime ? "Enabled" : "Disabled");
            API.consoleOutput("-> Logging: {0}", Logging ? "Enabled" : "Disabled");

            if (!File.Exists( GetDBPath() )) SQLiteConnection.CreateFile( GetDBPath() );

            // create tables if they don't exist
            using (SQLiteConnection sqlCon = new SQLiteConnection( string.Format("Data Source={0}", GetDBPath()) ))
            {
                using (SQLiteCommand sqlCmd = new SQLiteCommand(sqlCon))
                {
                    try {
                        sqlCon.Open();

                        sqlCmd.CommandText = "CREATE TABLE IF NOT EXISTS wallets (SocialClub NVARCHAR(20) PRIMARY KEY, Money BIGINT)";
                        sqlCmd.ExecuteNonQuery();

                        sqlCmd.CommandText = "CREATE TABLE IF NOT EXISTS wallet_logs (ID INTEGER PRIMARY KEY AUTOINCREMENT, SocialClub NVARCHAR(20), Amount BIGINT, Function VARCHAR(20), Date DATETIME)";
                        sqlCmd.ExecuteNonQuery();

                        sqlCon.Close();
                    } catch (SQLiteException e) {
                        API.consoleOutput("Init Error: #{0} - {1}", e.ErrorCode, e.Message);
                    }
                }
            }

            // load wallets of connected players
            foreach (Client player in API.getAllPlayers()) InitPlayer(player);
        }

        public void MoneyAPI_Exit()
        {
            foreach (Client player in SaveList) SaveWallet(player, true);

            SaveList.Clear();
            SaveList.TrimExcess();
        }

        public void MoneyAPI_Update()
        {
            if (AutosaveInterval == 0) return;
            if (DateTime.Now.Subtract(LastAutosave).Minutes >= AutosaveInterval)
            {
                int savedCount = 0;
                foreach (Client player in SaveList) if (SaveWallet(player, true)) savedCount++;
                if (savedCount > 0) API.consoleOutput("-> Autosaved {0} wallets.", savedCount);

                SaveList.Clear();
                SaveList.TrimExcess();

                LastAutosave = DateTime.Now;
            }
        }

        public void MoneyAPI_PlayerJoin(Client player)
        {
            InitPlayer(player);
        }

        public void MoneyAPI_PlayerLeave(Client player, string reason)
        {
            SaveWallet(player);
        }
    }
}