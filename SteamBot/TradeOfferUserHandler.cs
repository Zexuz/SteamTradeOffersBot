using SteamKit2;
using System;
using System.Collections.Generic;
using System.Linq;
using SteamAPI;
using SteamAPI.TradeOffers;
using SteamAPI.TradeOffers.Objects;

namespace SteamBot
{
    public class TradeOfferUserHandler : UserHandler
    {
        public TradeOfferUserHandler(Bot bot, SteamID sid) : base(bot, sid) { }

        public override void OnTradeOfferChecked(TradeOffer tradeOffer)
        {
            // polling has been completed once for our sent trade offer, and it is still active
            // this will always be a trade offer from the bot
        }

        public override void OnTradeOfferReceived(TradeOffer tradeOffer)
        {
            if (IsAdmin)
            {
                try
                {
                    // see documentation for more info on when TradeOfferSteamException is thrown
                    ulong tradeId;
                    if (TradeOffers.AcceptTrade(tradeOffer.Id, out tradeId))
                    {
                        // you can do something with tradeId if you need to
                    }
                }
                catch (TradeOfferSteamException ex)
                {
                    if (ex.ErrorCode == 11 | ex.ErrorCode == 16)
                    {
                        // trade offer might have been accepted still
                    }
                }     
            }
            else
            {
                try
                {
                    TradeOffers.DeclineTrade(tradeOffer.Id);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
        }

        public override void OnTradeOfferAccepted(TradeOffer tradeOffer)
        {
            var tradeOfferId = tradeOffer.Id;
            var myItems = tradeOffer.ItemsToGive;
            var userItems = tradeOffer.ItemsToReceive;

            Log.Info("Trade offer #{0} accepted. Items given: {1}, Items received: {2}", tradeOfferId, myItems.Length, userItems.Length);

            // myItems is now in user inventory
            // userItems is now in bot inventory
        }

        public override void OnTradeOfferDeclined(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} has been declined.", tradeOffer.Id);
        }

        public override void OnTradeOfferCanceled(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} has been canceled by bot.", tradeOffer.Id);
        }

        public override void OnTradeOfferInvalid(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} is invalid, with state: {1}.", tradeOffer.Id, tradeOffer.State);
        }

        public override void OnTradeOfferInEscrow(TradeOffer tradeOffer)
        {
            Log.Warn("Trade offer #{0} is in escrow until {1}.", tradeOffer.Id, tradeOffer.EscrowEndDate);
        }

        public override void OnTradeOfferConfirmed(TradeOffer tradeOffer)
        {
            Log.Success("Trade offer #{0} has been successfully confirmed.", tradeOffer.Id);
        }

        public override void OnTradeOfferFailedConfirmation(TradeOffer tradeOffer)
        {
            // confirmation failed, so cancel it just to be safe
            if (tradeOffer.IsOurOffer)
            {
                try
                {
                    TradeOffers.CancelTrade(tradeOffer);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
            else
            {
                try
                {
                    TradeOffers.DeclineTrade(tradeOffer);
                }
                catch (TradeOfferSteamException ex)
                {
                    var tradeErrorCode = ex.ErrorCode; // you can do something with this if you want
                }
            }
            Log.Warn("Trade offer #{0} failed to confirm. Cancelled the trade.");
        }

        public override void OnTradeOfferNoData(TradeOffer tradeOffer)
        {
            // Steam's GetTradeOffer/v1 API only gives data for the last 1000 received and 500 sent trade offers, so sometimes this will be called.
            // The only property from this trade offer object you can access is its ID. It's up to you how you want to handle it.
            // Trade offers in this state will be gone once the bot is restarted, so this will only be called once.
            // If your bot is offline when Steam loses data about the trade offer, this will never be called.

            Log.Warn("No data from Steam for trade offer #{0}!", tradeOffer.Id);
        }

        public override void OnMessage(string message, EChatEntryType type)
        {
            if (IsAdmin)
            {
                if (message == "auth")
                {
                    Bot.SteamFriends.SendChatMessage(OtherSID, EChatEntryType.ChatMsg, Bot.SteamGuardAccount.GenerateSteamGuardCode());
                }
                else if (message == "inventory example")
                {
                    // EXAMPLE: working with inventories
                    var tradeOffer = TradeOffers.CreateTrade(OtherSID);
                    var appIdsAndContextId = new Dictionary<int,int>
                    {
                        {(int) GenericInventory.AppId.CSGO, (int) GenericInventory.ContextId.CSGO}
                    };
                    var inventories = FetchInventories(Bot.SteamClient.SteamID, appIdsAndContextId);
                    var csgoInventory = inventories.GetInventory(730, 2);
                    foreach (var item in csgoInventory.Items)
                    {
                        // if you need info about the item, such as name, etc, use GetItemDescription
                        var description = csgoInventory.GetItemDescription(item);
                        Log.Info("This item is: {0}.", description.Name);
                        tradeOffer.AddMyItem(730, 2, item.AssetId);
                        break;
                    }
                    try
                    {
                        // TradeOfferSteamException will be thrown when sending fails
                        var tradeOfferIdWithToken = tradeOffer.SendTradeWithToken("message", "token");
                        Log.Success("Trade offer sent: Offer ID " + tradeOfferIdWithToken);
                    }
                    catch (TradeOfferSteamException ex)
                    {
                        if (ex.ErrorCode == 11 || ex.ErrorCode == 16)
                        {
                            // trade offer might have been sent even though there was an error                            
                        }
                    }                    
                }
                else if (message == "trade offer example")
                {
                    // EXAMPLE: creating a new trade offer
                    var tradeOffer = TradeOffers.CreateTrade(OtherSID);

                    //tradeOffer.AddMyItem(0, 0, 0);
                    
                    try
                    {
                        // sending trade offer without token (only for Steam friends)
                        var tradeOfferId = tradeOffer.SendTrade("message");
                        Log.Success("Trade offer sent: Offer ID " + tradeOfferId);
                    }
                    catch (TradeOfferSteamException ex)
                    {
                        if (ex.ErrorCode == 11 || ex.ErrorCode == 16)
                        {
                            // trade offer might have been sent even though there was an error
                        }
                    }                    

                    try
                    {
                        // sending trade offer with token
                        // "token" should be replaced with the actual token from the other user
                        var tradeOfferIdWithToken = tradeOffer.SendTradeWithToken("message", "token");
                        Log.Success("Trade offer sent: Offer ID " + tradeOfferIdWithToken);

                    }
                    catch (TradeOfferSteamException ex)
                    {
                        if (ex.ErrorCode == 11 || ex.ErrorCode == 16)
                        {
                            // trade offer might have been sent even though there was an error
                        }
                    }
                }       
            }
        }

        public override bool OnGroupAdd() { return false; }

        public override bool OnFriendAdd() { return IsAdmin; }

        public override void OnFriendRemove() { }

        public override void OnLoginCompleted() { }
    }
}
