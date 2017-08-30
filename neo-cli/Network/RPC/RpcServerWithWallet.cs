﻿using Neo.Core;
using Neo.IO.Json;
using Neo.Wallets;
using System.Collections.Generic;
using System.Linq;

namespace Neo.Network.RPC
{
    internal class RpcServerWithWallet : RpcServer
    {
        public RpcServerWithWallet(LocalNode localNode)
            : base(localNode)
        {
        }

        protected override JObject Process(string method, JArray _params)
        {
            switch (method)
            {
                case "getaddressbalance":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied.");
                    else
                    {
                        var address = _params[0].AsString();
                        var received = Program.Wallet.GetCoins()
                            .Where(c => c.Address == address)
                            .GroupBy(r => r.Output.AssetId);

                        JObject json = new JObject();
                        json["balances"] = new JArray(received.Select(p =>
                        {
                            JObject balance = new JObject();
                            balance["asset"] = p.Key.ToString();
                            balance["unconfirmed"] = p.Where(o => !o.State.HasFlag(CoinState.Confirmed)).Sum(o => o.Output.Value).ToString();
                            balance["confirmed"] = p.Where(o => o.State.HasFlag(CoinState.Confirmed)).Sum(o => o.Output.Value).ToString();
                            balance["spendable"] = p.Where(o => o.State.HasFlag(CoinState.Confirmed) && !o.State.HasFlag(CoinState.Spent)).Sum(o => o.Output.Value).ToString();
                            return balance;
                        }));

                        return json;
                    }
                case "getbalance":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied.");
                    else
                    {
                        UInt256 assetId = UInt256.Parse(_params[0].AsString());
                        IEnumerable<Coin> coins = Program.Wallet.GetCoins().Where(p => !p.State.HasFlag(CoinState.Spent) && p.Output.AssetId.Equals(assetId));
                        JObject json = new JObject();
                        json["balance"] = coins.Sum(p => p.Output.Value).ToString();
                        json["confirmed"] = coins.Where(p => p.State.HasFlag(CoinState.Confirmed)).Sum(p => p.Output.Value).ToString();
                        return json;
                    }
                case "sendtoaddress":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        UInt256 assetId = UInt256.Parse(_params[0].AsString());
                        UInt160 scriptHash = Wallet.ToScriptHash(_params[1].AsString());
                        Fixed8 value = Fixed8.Parse(_params[2].AsString());
                        if (value <= Fixed8.Zero)
                            throw new RpcException(-32602, "Invalid params");
                        Fixed8 fee = _params.Count >= 4 ? Fixed8.Parse(_params[3].AsString()) : Fixed8.Zero;
                        if (value < Fixed8.Zero)
                            throw new RpcException(-32602, "Invalid params");
                        UInt160 change_address = _params.Count >= 5 ? Wallet.ToScriptHash(_params[4].AsString()) : null;
                        ContractTransaction tx = Program.Wallet.MakeTransaction(new ContractTransaction
                        {
                            Outputs = new[]
                            {
                                new TransactionOutput
                                {
                                    AssetId = assetId,
                                    Value = value,
                                    ScriptHash = scriptHash
                                }
                            }
                        }, change_address: change_address, fee: fee);
                        if (tx == null)
                            throw new RpcException(-300, "Insufficient funds");
                        SignatureContext context = new SignatureContext(tx);
                        Program.Wallet.Sign(context);
                        if (context.Completed)
                        {
                            tx.Scripts = context.GetScripts();
                            Program.Wallet.SaveTransaction(tx);
                            LocalNode.Relay(tx);
                            return tx.ToJson();
                        }
                        else
                        {
                            return context.ToJson();
                        }
                    }
                case "sendmany":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        JArray to = (JArray)_params[0];
                        if (to.Count == 0)
                            throw new RpcException(-32602, "Invalid params");
                        TransactionOutput[] outputs = new TransactionOutput[to.Count];
                        for (int i = 0; i < to.Count; i++)
                        {
                            outputs[i] = new TransactionOutput
                            {
                                AssetId = UInt256.Parse(to[i]["asset"].AsString()),
                                Value = Fixed8.Parse(to[i]["value"].AsString()),
                                ScriptHash = Wallet.ToScriptHash(to[i]["address"].AsString())
                            };
                            if (outputs[i].Value <= Fixed8.Zero)
                                throw new RpcException(-32602, "Invalid params");
                        }
                        Fixed8 fee = _params.Count >= 2 ? Fixed8.Parse(_params[1].AsString()) : Fixed8.Zero;
                        if (fee < Fixed8.Zero)
                            throw new RpcException(-32602, "Invalid params");
                        UInt160 change_address = _params.Count >= 3 ? Wallet.ToScriptHash(_params[2].AsString()) : null;
                        ContractTransaction tx = Program.Wallet.MakeTransaction(new ContractTransaction
                        {
                            Outputs = outputs
                        }, change_address: change_address, fee: fee);
                        if (tx == null)
                            throw new RpcException(-300, "Insufficient funds");
                        SignatureContext context = new SignatureContext(tx);
                        Program.Wallet.Sign(context);
                        if (context.Completed)
                        {
                            tx.Scripts = context.GetScripts();
                            Program.Wallet.SaveTransaction(tx);
                            LocalNode.Relay(tx);
                            return tx.ToJson();
                        }
                        else
                        {
                            return context.ToJson();
                        }
                    }
                case "getnewaddress":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        KeyPair key = Program.Wallet.CreateKey();
                        Contract contract = Program.Wallet.GetContracts(key.PublicKeyHash).First(p => p.IsStandard);
                        return contract.Address;
                    }
                case "dumpprivkey":
                    if (Program.Wallet == null)
                        throw new RpcException(-400, "Access denied");
                    else
                    {
                        UInt160 scriptHash = Wallet.ToScriptHash(_params[0].AsString());
                        KeyPair key = Program.Wallet.GetKeyByScriptHash(scriptHash);
                        return key.Export();
                    }
                default:
                    return base.Process(method, _params);
            }
        }
    }
}
