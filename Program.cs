using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using NEL_FutureDao_Contract.lib;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NEL_FutureDao_Contract
{
    /// <summary>
    /// 
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Program start");
            startTask();
            //
            Console.WriteLine("Program init finished!");
            while (true)
            {
                Thread.Sleep(3000);
            }
        }
        static void startTask()
        {
            Config.initConfig("config.json");
            //
            startRun(new ContractTask("ContractTask").run);
            startRun(new PriceTask("PriceTask").run);
            startRun(new ProposalTask("ProposalTask").run);
        }
        static void startRun(Action action)
        {
            Task.Run(action);
        }
    }

    class Config
    {
        public static string mongodbConnStr = "";
        public static string mongodbDatabase = "";
        public static string ethOriginRecordCol = "";
        public static string ethOriginStateCol = "";
        public static string ethContractInfoCol = "";
        public static string ethContractStateCol = "ethContractState";
        public static string ethPriceStateCol = "ethPriceState";
        public static string ethVoteStateCol = "ethVoteState";
        public static string ethRecordCol = "ethRecord";
        public static int interval;

        public static void initConfig(string path)
        {
            JObject root = JObject.Parse(File.ReadAllText(path));
            mongodbConnStr = root["mongodbConnStr"].ToString();
            mongodbDatabase = root["mongodbDatabase"].ToString();
            ethOriginRecordCol = root["ethOriginRecordCol"].ToString();
            ethOriginStateCol = root["ethOriginStateCol"].ToString();
            ethContractInfoCol = root["ethContractInfoCol"].ToString();
            ethContractStateCol = root["ethContractStateCol"].ToString();
            ethPriceStateCol = root["ethPriceStateCol"].ToString();
            ethVoteStateCol = root["ethVoteStateCol"].ToString();
            ethRecordCol = root["ethRecordCol"].ToString();
            interval = (int)root["interval"];
            Console.WriteLine("ethPool.{0}:{1}", "mongodbConnStr", mongodbConnStr);
            Console.WriteLine("ethPool.{0}:{1}", "mongodbDatabase", mongodbDatabase);
            Console.WriteLine("ethPool.{0}:{1}", "ethOriginRecordCol", ethOriginRecordCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethOriginStateCol", ethOriginStateCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethContractInfoCol", ethContractInfoCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethContractStateCol", ethContractStateCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethPriceStateCol", ethPriceStateCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethVoteStateCol", ethVoteStateCol);
            Console.WriteLine("ethPool.{0}:{1}", "ethRecordCol", ethRecordCol);
            Console.WriteLine("ethPool.{0}:{1}", "interval", interval);
        }

    }

    class LogHelper
    {
        public static void log(string name, Exception ex)
        {
            File.AppendAllText(name+".log", string.Format("{0} {1} failed, errMsg:{2}, errStack:{3}", System.DateTime.Now.ToString("u"), name, ex.Message, ex.StackTrace));
        }
    }
    class ContractTask
    {
        private string name;
        private static MongoHelper mh = new MongoHelper();
        private static string mongodbConnStr = Config.mongodbConnStr;
        private static string mongodbDatabase = Config.mongodbDatabase;
        private static string ethOriginRecordCol = Config.ethOriginRecordCol;
        private static string ethOriginStateCol = Config.ethOriginStateCol;
        private static string ethContractStateCol = Config.ethContractStateCol;
        private static string ethContractInfoCol = Config.ethContractInfoCol;
        private static string ethRecordCol = Config.ethRecordCol;

        public ContractTask(string name) { this.name = name; }
        public void run()
        {
            Console.WriteLine("{0} start running...", name);
            while (true)
            {
                try
                {
                    loop();
                    Thread.Sleep(Config.interval);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("{0} failed, errMsg:{1}, errStack:{2}", name, ex.Message, ex.StackTrace);
                    LogHelper.log(name, ex);
                    Thread.Sleep(Config.interval * 10);
                }

            }
        }
        public void loop()
        {
            long rh = GetRCounter();
            long lh = GetLCounter();
            for (long index = lh + 1; index <= rh; ++index)
            {
                string findStr = new JObject { { "blockNumner", index }, { "eventName", "OnCreate" } }.ToString();
                var queryRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginStateCol, findStr);
                foreach (var item in queryRes)
                {
                    var chash = item["address"].ToString();
                    var eventName = item["eventName"].ToString();

                    var txid = item["transactionHash"].ToString();
                    var blockindex = long.Parse(item["blockNumner"].ToString());
                    var blocktime = long.Parse(item["blockTime"].ToString());
                    var cIndex = ((JArray)item["values"])[0]["value"].ToString();
                    var cAddress = ((JArray)item["values"])[1]["value"].ToString();
                    var cName = ((JArray)item["values"])[2]["value"].ToString();
                    var fundHash = ((JArray)item["values"])[3]["value"].ToString();
                    var voteHash = ((JArray)item["values"])[4]["value"].ToString();

                    // 若没有，则直接入库；否则更新入库
                    findStr = new JObject { { "fundHash", fundHash }, { "voteHash", voteHash } }.ToString();
                    var subRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethContractStateCol, findStr);
                    if (subRes == null || subRes.Count == 0)
                    {
                        var newdata = new JObject {
                            { "chash", chash},
                            { "eventName", eventName},
                            { "txid", txid},
                            { "blockindex", blockindex},
                            { "blocktime", blocktime},
                            { "cIndex", cIndex},
                            { "cAddress", cAddress},
                            { "cName", cName},
                            { "fundHash", fundHash},
                            { "voteHash", voteHash}
                        }.ToString();
                        mh.PutData(mongodbConnStr, mongodbDatabase, ethContractStateCol, newdata);
                    }
                }
                //
                updateCounter(index);
                log(index, rh);
            }

            //
            updateHashLoop();
        }
        private void updateHashLoop()
        {
            string findStr = new JObject { { "hash", "" }, { "voteHash", "" } }.ToString();
            string fieldStr = new JObject { { "txid", 1 } }.ToString();
            var queryRes = mh.GetDataWithField(mongodbConnStr, mongodbDatabase, ethContractInfoCol, fieldStr, findStr);
            if (queryRes != null && queryRes.Count > 0)
            {
                foreach (var item in queryRes)
                {
                    string txid = item["txid"].ToString();
                    var subfindStr = new JObject { { "txid", txid } }.ToString();
                    var subfieldStr = new JObject { { "fundHash", 1 }, { "voteHash", 1 } }.ToString();
                    var subres = mh.GetDataWithField(mongodbConnStr, mongodbDatabase, ethContractStateCol, subfieldStr, subfindStr);
                    if (subres != null && subres.Count > 0)
                    {
                        var fundHash = subres[0]["fundHash"].ToString();
                        var voteHash = subres[0]["voteHash"].ToString();
                        var updateStr = new JObject { { "$set", new JObject { { "hash", fundHash }, { "voteHash", voteHash } } } }.ToString();
                        mh.UpdateData(mongodbConnStr, mongodbDatabase, ethContractInfoCol, updateStr, subfindStr);
                    }
                }
            }
        }


        private void log(long index, long rh)
        {
            Console.WriteLine("{0} processed: {1}/{2}", name, index, rh);
        }
        private void updateCounter(long counter)
        {
            string findStr = new JObject { { "key", ethContractStateCol } }.ToString();
            if (mh.GetDataCount(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr) == 0)
            {
                var newdata = new JObject { { "key", ethContractStateCol }, { "counter", counter } }.ToString();
                mh.PutData(mongodbConnStr, mongodbDatabase, ethRecordCol, newdata);
                return;
            }
            var updateData = new JObject { { "$set", new JObject { { "counter", counter } } } }.ToString();
            mh.UpdateData(mongodbConnStr, mongodbDatabase, ethRecordCol, updateData, findStr);
        }
        private long GetLCounter()
        {
            string findStr = new JObject { { "key", ethContractStateCol } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr);
            if (res != null && res.Count > 0) return long.Parse(res[0]["counter"].ToString());
            return -1;
        }
        private long GetRCounter()
        {
            string findStr = new JObject { { "counter", "blockNumber" } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginRecordCol, findStr); ;
            if (res != null && res.Count > 0) return long.Parse(res[0]["lastIndex"].ToString());
            return -1;
        }
    }

    // 价格数据
    class PriceTask
    {
        private string name;
        private static MongoHelper mh = new MongoHelper();
        private static string mongodbConnStr = Config.mongodbConnStr;
        private static string mongodbDatabase = Config.mongodbDatabase;
        private static string ethOriginRecordCol = Config.ethOriginRecordCol;
        private static string ethOriginStateCol = Config.ethOriginStateCol;
        private static string ethContractStateCol = Config.ethContractStateCol;
        private static string ethPriceStateCol = Config.ethPriceStateCol;
        private static string ethRecordCol = Config.ethRecordCol;

        public PriceTask(string name) { this.name = name; }
        public void run()
        {
            Console.WriteLine("{0} start running...", name);
            while (true)
            {
                try
                {
                    loop();
                    Thread.Sleep(Config.interval);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("{0} failed, errMsg:{1}, errStack:{2}", name, ex.Message, ex.StackTrace);
                    LogHelper.log(name, ex);
                    Thread.Sleep(Config.interval * 10);
                }

            }
        }
        public void loop()
        {
            long rh = GetRCounter();
            long lh = GetLCounter();
            long rl = GetRLCounter();
            if (rh > rl) rh = rl;
            for (long index = lh + 1; index <= rh; ++index)
            {
                string findStr = new JObject { { "blockNumner", index } }.ToString();
                var queryRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginStateCol, findStr);
                foreach (var item in queryRes)
                {
                    var hash = item["address"].ToString();
                    var votehash = getVoteHash(hash);
                    var eventName = item["eventName"].ToString();
                    if (eventName != "OnBuy" && eventName != "OnSell") continue;

                    var txid = item["transactionHash"].ToString();
                    var blockindex = long.Parse(item["blockNumner"].ToString());
                    var blocktime = long.Parse(item["blockTime"].ToString());
                    var opAddress = ((JArray)item["values"])[0]["value"].ToString();
                    var ethAmount = long.Parse(((JArray)item["values"])[1]["value"].ToString());
                    var fndAmount = long.Parse(((JArray)item["values"])[2]["value"].ToString());

                    var price = fndAmount == 0 ? 0 : ethAmount / fndAmount;
                    var perFrom24h = getPerFrom24h(hash, price, blocktime);

                    // 若没有，则直接入库；否则更新入库
                    findStr = new JObject { { "txid", txid } }.ToString();
                    var subRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethPriceStateCol, findStr);
                    if (subRes == null || subRes.Count == 0)
                    {
                        var newdata = new JObject {
                            { "hash", hash},
                            { "votehash", votehash},
                            { "eventName", eventName},
                            { "txid", txid},
                            { "blockindex", blockindex},
                            { "blocktime", blocktime},
                            { "address", opAddress},
                            { "ethAmount", ethAmount},
                            { "fndAmount", fndAmount},
                            { "price", price},
                            { "perFrom24h", perFrom24h},
                        }.ToString();
                        mh.PutData(mongodbConnStr, mongodbDatabase, ethPriceStateCol, newdata);
                    }
                }
                //
                updateCounter(index);
                log(index, rh);
            }
        }

        private long seconds24H = 24 * 60 * 60;
        private string getPerFrom24h(string hash, long price, long time)
        {
            double priceNew = price;
            double priceOld = price;
            string findStr = new JObject { { "hash", hash }, { "blocktime", new JObject { { "$gte", time - seconds24H } } } }.ToString();
            string sortStr = new JObject { { "blocktime", 1 } }.ToString();
            var queryRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethPriceStateCol, findStr, sortStr, 0, 1);
            if (queryRes != null && queryRes.Count > 0)
            {
                priceOld = (double)queryRes[0]["price"];
            }
            var rr = (priceNew - priceOld) * 100 / priceOld;
            var cc = rr.ToString("0.00");
            return cc;
        }

        private void log(long index, long rh)
        {
            Console.WriteLine("{0} processed: {1}/{2}", name, index, rh);
        }
        private void updateCounter(long counter)
        {
            string findStr = new JObject { { "key", ethPriceStateCol } }.ToString();
            if (mh.GetDataCount(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr) == 0)
            {
                var newdata = new JObject { { "key", ethPriceStateCol }, { "counter", counter } }.ToString();
                mh.PutData(mongodbConnStr, mongodbDatabase, ethRecordCol, newdata);
                return;
            }
            var updateData = new JObject { { "$set", new JObject { { "counter", counter } } } }.ToString();
            mh.UpdateData(mongodbConnStr, mongodbDatabase, ethRecordCol, updateData, findStr);
        }
        private long GetLCounter()
        {
            string findStr = new JObject { { "key", ethPriceStateCol } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr);
            if (res != null && res.Count > 0) return long.Parse(res[0]["counter"].ToString());
            return -1;
        }
        private long GetRCounter()
        {
            string findStr = new JObject { { "counter", "blockNumber" } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginRecordCol, findStr); ;
            if (res != null && res.Count > 0) return long.Parse(res[0]["lastIndex"].ToString());
            return -1;
        }
        private long GetRLCounter()
        {
            string findStr = new JObject { { "key", ethContractStateCol } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr);
            if (res != null && res.Count > 0) return long.Parse(res[0]["counter"].ToString());
            return -1;
        }

        private string getVoteHash(string fundHash)
        {
            string findStr = new JObject { { "fundHash", fundHash } }.ToString();
            string fieldStr = new JObject { { "voteHash", 1 } }.ToString();
            var queryRes = mh.GetDataWithField(mongodbConnStr, mongodbDatabase, ethContractStateCol, fieldStr, findStr); ;
            if (queryRes != null && queryRes.Count > 0) return queryRes[0]["voteHash"].ToString();
            return "";
        }
    }

    // 提案数据
    class ProposalTask
    {
        private string name;
        private static MongoHelper mh = new MongoHelper();
        private static string mongodbConnStr = Config.mongodbConnStr;
        private static string mongodbDatabase = Config.mongodbDatabase;
        private static string ethOriginRecordCol = Config.ethOriginRecordCol;
        private static string ethOriginStateCol = Config.ethOriginStateCol;
        private static string ethContractStateCol = Config.ethContractStateCol;
        private static string ethVoteStateCol = Config.ethVoteStateCol;
        private static string ethRecordCol = Config.ethRecordCol;

        public ProposalTask(string name) { this.name = name; }
        public void run()
        {
            Console.WriteLine("{0} start running...", name);
            while (true)
            {
                try
                {
                    loop();
                    Thread.Sleep(Config.interval);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("{0} failed, errMsg:{1}, errStack:{2}", name, ex.Message, ex.StackTrace);
                    LogHelper.log(name, ex);
                    Thread.Sleep(Config.interval * 10);
                }

            }
        }
        public void loop()
        {
            long rh = GetRCounter();
            long lh = GetLCounter();
            long rl = GetRLCounter();
            if (rh > rl) rh = rl;
            for (long index = lh + 1; index <= rh; ++index)
            {
                string findStr = new JObject { { "blockNumner", index } }.ToString();
                var queryRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginStateCol, findStr);
                foreach (var item in queryRes)
                {
                    var voteHash = item["address"].ToString(); // 提案哈希
                    var fundhash = getfundHash(voteHash);
                    var eventName = item["eventName"].ToString();
                    if (eventName != "OnApplyProposal" && eventName != "OnVote" && eventName != "OnAbort") continue;

                    var txid = item["transactionHash"].ToString();
                    var blockindex = long.Parse(item["blockNumner"].ToString());
                    var blocktime = long.Parse(item["blockTime"].ToString());
                    if (eventName == "OnApplyProposal")
                    {
                        var proposalIndex = long.Parse(((JArray)item["values"])[0]["value"].ToString());
                        var proposalName = ((JArray)item["values"])[1]["value"].ToString();
                        var proposer = ((JArray)item["values"])[2]["value"].ToString();
                        var startTime = long.Parse(((JArray)item["values"])[3]["value"].ToString());
                        var recipient = ((JArray)item["values"])[4]["value"].ToString();
                        var value = BigInteger.Parse(((JArray)item["values"])[5]["value"].ToString().format("e"));
                        //var value = ((JArray)item["values"])[5]["value"].ToString().format("e");
                        var timeConsuming = BigInteger.Parse(((JArray)item["values"])[6]["value"].ToString());
                        var detail = ((JArray)item["values"])[7]["value"].ToString();
                        var voteYesCount = 0;
                        var voteNotCount = 0;
                        var proposalState = ProposalState.Voting;
                        var newdata = new JObject {
                            { "hash", fundhash},
                            { "voteHash", voteHash},
                            { "proposalBlockNumber", blockindex},
                            { "proposalBlockTime", blocktime},
                            { "proposalTxid", txid},
                            { "proposalIndex", proposalIndex},
                            { "proposalName", proposalName},
                            { "proposer", proposer},
                            { "startTime", startTime},
                            { "recipient", recipient},
                            { "value", value.ToString()},
                            { "timeConsuming", timeConsuming.ToString()},
                            { "valueAvg", (value/timeConsuming).ToString()},
                            { "displayMethod", timeConsuming > 0 ? DisplayMethod.ByDays: DisplayMethod.ByOne},
                            { "detail", detail},
                            { "voteYesCount", voteYesCount},
                            { "voteNotCount", voteNotCount},
                            { "proposalState", proposalState }
                        }.ToString();
                        findStr = new JObject { { "hash", fundhash }, { "voteHash", voteHash }, { "proposalIndex", proposalIndex } }.ToString();
                        if (mh.GetDataCount(mongodbConnStr, mongodbDatabase, ethVoteStateCol, findStr) == 0)
                        {
                            mh.PutData(mongodbConnStr, mongodbDatabase, ethVoteStateCol, newdata);
                        }
                        continue;
                    }
                    if (eventName == "OnVote")
                    {
                        var proposalIndex = long.Parse(((JArray)item["values"])[0]["value"].ToString());
                        var VoteFlag = long.Parse(((JArray)item["values"])[1]["value"].ToString());
                        var VoteCount = long.Parse(((JArray)item["values"])[2]["value"].ToString());
                        findStr = new JObject { { "hash", fundhash }, { "voteHash", voteHash }, { "proposalIndex", proposalIndex } }.ToString();
                        queryRes = mh.GetData(mongodbConnStr, mongodbDatabase, ethVoteStateCol, findStr);
                        if (queryRes != null && queryRes.Count > 0)
                        {
                            var key = "";
                            var val = 0L;
                            if (VoteFlag == VoteYesOrNot.Yes)
                            {
                                key = "voteYesCount";
                            }
                            if (VoteFlag == VoteYesOrNot.Not)
                            {
                                key = "voteNotCount";
                            }
                            if (key != "")
                            {
                                val = long.Parse(queryRes[0][key].ToString()) + VoteCount;
                                mh.UpdateData(mongodbConnStr, mongodbDatabase, ethVoteStateCol, new JObject { { "$set", new JObject { { key, val } } } }.ToString(), findStr);
                            }
                        }
                        continue;
                    }
                    if (eventName == "OnAbort")
                    {
                        var key = "proposalState";
                        var val = ProposalState.Abort;
                        var proposalIndex = long.Parse(((JArray)item["values"])[0]["value"].ToString());
                        findStr = new JObject { { "hash", fundhash }, { "voteHash", voteHash }, { "proposalIndex", proposalIndex } }.ToString();
                        mh.UpdateData(mongodbConnStr, mongodbDatabase, ethVoteStateCol, new JObject { { "$set", new JObject { { key, val } } } }.ToString(), findStr);
                        continue;
                    }
                }
                //
                updateCounter(index);
                log(index, rh);
            }
        }

        private void log(long index, long rh)
        {
            Console.WriteLine("{0} processed: {1}/{2}", name, index, rh);
        }
        private void updateCounter(long counter)
        {
            string findStr = new JObject { { "key", ethVoteStateCol } }.ToString();
            if (mh.GetDataCount(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr) == 0)
            {
                var newdata = new JObject { { "key", ethVoteStateCol }, { "counter", counter } }.ToString();
                mh.PutData(mongodbConnStr, mongodbDatabase, ethRecordCol, newdata);
                return;
            }
            var updateData = new JObject { { "$set", new JObject { { "counter", counter } } } }.ToString();
            mh.UpdateData(mongodbConnStr, mongodbDatabase, ethRecordCol, updateData, findStr);
        }
        private long GetLCounter()
        {
            string findStr = new JObject { { "key", ethVoteStateCol } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr);
            if (res != null && res.Count > 0) return long.Parse(res[0]["counter"].ToString());
            return -1;
        }
        private long GetRCounter()
        {
            string findStr = new JObject { { "counter", "blockNumber" } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethOriginRecordCol, findStr); ;
            if (res != null && res.Count > 0) return long.Parse(res[0]["lastIndex"].ToString());
            return -1;
        }
        private long GetRLCounter()
        {
            string findStr = new JObject { { "key", ethContractStateCol } }.ToString();
            var res = mh.GetData(mongodbConnStr, mongodbDatabase, ethRecordCol, findStr);
            if (res != null && res.Count > 0) return long.Parse(res[0]["counter"].ToString());
            return -1;
        }
        private string getfundHash(string voteHash)
        {
            string findStr = new JObject { { "voteHash", voteHash } }.ToString();
            string fieldStr = new JObject { { "fundHash", 1 } }.ToString();
            var queryRes = mh.GetDataWithField(mongodbConnStr, mongodbDatabase, ethContractStateCol, fieldStr, findStr); ;
            if (queryRes != null && queryRes.Count > 0) return queryRes[0]["fundHash"].ToString();
            return "";
        }
    }

    class DisplayMethod
    {
        public const string ByDays = "days";    // "按天接收";
        public const string ByOne = "one";      // "立刻接收";
    }
    class ProposalState
    {
        public const int Voting = 0;
        public const int Execting = 1;
        public const int Finish = 2;
        public const int Abort = 3;
    }
    class VoteYesOrNot
    {
        public const int Yes = 1;
        public const int Not = 2;
    }
    //
    [BsonIgnoreExtraElements]
    class EthPoolInfo
    {
        public ObjectId _id { get; set; }
        public string ethPoolHash { get; set; }
        public string ethBalance { get; set; }
        public string ethBalance30 { get; set; }
        public string ethBalance70 { get; set; }
        public string buyPrice { get; set; }
        public string sellPrie { get; set; }
        public string perFrom24h { get; set; }
        public string lastBlockNumber { get; set; }
    }
    public static class DaoHelper {
        public static string format(this string s, string format/* 8e+18 */)
        {
            s = s.ToLower();
            int index = s.IndexOf(format);
            if(index != -1)
            {
                var pw = s.Substring(index + 2);
                var st = s.Substring(0, index);
                return st + fullZero(int.Parse(pw));
            }
            return s;
        }
        private static string fullZero(int digit)
        {
            StringBuilder sb = new StringBuilder();
            for(int i=0; i<digit; ++i)
            {
                sb.Append("0");
            }
            return sb.ToString();

        }
    }
    


}
