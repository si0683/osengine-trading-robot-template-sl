using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

/* Description
trading robot for osengine

TODO: описание стратегии

Buy:
TODO

Sell:
TODO

Exit from buy:
TODO

Exit from sell:
TODO

Volume:
Calculated as % of deposit risk per trade, accounting for the distance to the stop,
slippage and commission. Supports SPOT / LinearPerpetual, Stocks MOEX, InversFutures, Futures MOEX.
*/

namespace OsEngine.Robots
{
    [Bot("TemplateRobotAdvanced")]
    public class TemplateRobotAdvanced : BotPanel
    {
        // ── Вкладка ──────────────────────────────────────────────────────────────────
        private readonly BotTabSimple _tab;

        // ── Базовые параметры ────────────────────────────────────────────────────────
        private readonly StrategyParameterString _regime;
        private readonly StrategyParameterString _tradeLogOnOff;

        // ── Параметры объёма ─────────────────────────────────────────────────────────
        private readonly StrategyParameterString _modeTrade;
        private readonly StrategyParameterString _assetNameCurrent;
        private readonly StrategyParameterDecimal _volumeLong;
        private readonly StrategyParameterDecimal _volumeShort;
        private readonly StrategyParameterDecimal _minVolumeTester;
        private readonly StrategyParameterDecimal _slippagePercent;
        private readonly StrategyParameterDecimal _feePercent;
        private readonly StrategyParameterInt _bondDaysToMaturity;
        private readonly StrategyParameterButton _tradePeriodsShowDialogButton;

        // ── Параметры стопа и тейка ──────────────────────────────────────────────────
        //
        //   Используются только если вы хотите фиксированный % стоп/тейк.
        //   Если стоп рассчитывается динамически (по индикатору / свингу) —
        //   передавайте цену напрямую в CalcVolume() и CloseAtStop().
        //
        private readonly StrategyParameterDecimal _stopPercent;    // % от цены входа
        private readonly StrategyParameterDecimal _profitPercent;  // % от цены входа (0 = не использовать)
        private readonly StrategyParameterBool _useTrailingStop;   // двигать стоп в б/у и далее

        // ── Параметры неторгового времени ────────────────────────────────────────────
        private readonly StrategyParameterInt _timeZoneUtc;
        private bool _nonTradePeriodLogged;
        private readonly NonTradePeriods _tradePeriods;

        // ── TODO: добавить параметры индикаторов здесь ───────────────────────────────
        // private readonly StrategyParameterInt _lengthMyIndicator;

        // ── TODO: добавить индикаторы здесь ──────────────────────────────────────────
        // private Aindicator _myIndicator;

        // ── Рабочие переменные расчёта объёма ────────────────────────────────────────
        // Синхронизированные копии параметров GUI (обновляются через SyncParams)
        private decimal _curVolumeLong;
        private decimal _curVolumeShort;
        private decimal _curMinVolumeTester;
        private decimal _curSlippagePercent;
        private decimal _curFeePercent;
        private int _curBondDaysToMaturity;
        private int _curTimeZoneUtc;
        private decimal _curStopPercent;
        private decimal _curProfitPercent;
        private bool _curUseTrailingStop;

        // ── Словарь стопов ───────────────────────────────────────────────────────────
        //
        //   Ключ     = order.NumberUser — уникальный int, присваивается движком при
        //              создании ордера внутри BuyAtLimit/SellAtLimit, до отправки на биржу.
        //   Значение = начальная цена стопа, рассчитанная для этой конкретной заявки.
        //
        //   Запись добавляется сразу после BuyAtLimit (синхронно, поток свечей).
        //   Запись удаляется в SetStopAndProfit (асинхронно, поток событий биржи),
        //   либо в OnOpeningFail (ордер отклонён биржей).
        //   ConcurrentDictionary обеспечивает потокобезопасность без явных lock-ов.
        //
        private readonly ConcurrentDictionary<int, StopProfitPair> _stopByOrderId
            = new ConcurrentDictionary<int, StopProfitPair>();

        // Пара стоп + тейк для одного ордера
        private struct StopProfitPair
        {
            public decimal StopPrice;
            public decimal ProfitPrice; // 0 = не использовать
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   КОНСТРУКТОР
        // ════════════════════════════════════════════════════════════════════════════

        public TemplateRobotAdvanced(string name, StartProgram startProgram) : base(name, startProgram)
        {
            _tradePeriods = new NonTradePeriods(name);
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1Start = new TimeOfDay { Hour = 0, Minute = 0 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1End = new TimeOfDay { Hour = 10, Minute = 5 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod1OnOff = true;
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2Start = new TimeOfDay { Hour = 13, Minute = 54 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2End = new TimeOfDay { Hour = 14, Minute = 6 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod2OnOff = false;
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3Start = new TimeOfDay { Hour = 18, Minute = 1 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3End = new TimeOfDay { Hour = 23, Minute = 58 };
            _tradePeriods.NonTradePeriodGeneral.NonTradePeriod3OnOff = true;
            _tradePeriods.TradeInSunday = false;
            _tradePeriods.TradeInSaturday = false;
            _tradePeriods.Load();

            TabCreate(BotTabType.Simple);
            _tab = TabsSimple[0];

            // ── Кнопка настройки неторговых периодов ─────────────────────────────────
            _tradePeriodsShowDialogButton = CreateParameterButton("Non trade periods", "Base");
            _tradePeriodsShowDialogButton.UserClickOnButtonEvent += () => _tradePeriods.ShowDialog();

            // ── Режим и лог ──────────────────────────────────────────────────────────
            _regime = CreateParameter("Regime", "Off",
                new[] { "Off", "On", "LONG-POS", "SHORT-POS", "CLOSE-POS" }, "Base");
            _timeZoneUtc = CreateParameter("Time zone UTC", 4, -24, 24, 1, "Base");
            _tradeLogOnOff = CreateParameter("Trade debug log", "Off", new[] { "On", "Off" }, "Base");

            // ── Объём ────────────────────────────────────────────────────────────────
            _modeTrade = CreateParameter("Trade Section",
                "SPOT и LinearPerpetual",
                new[] { "SPOT и LinearPerpetual", "InversFutures", "Stocks MOEX", "Futures MOEX", "Bonds MOEX" }, "Base");
            _assetNameCurrent = CreateParameter("Deposit Asset", "USDT",
                new[] { "USDT", "USDC", "USD", "RUB", "EUR", "BTC", "ETH", "XRP", "LTC", "SOL", "Prime" }, "Base");
            _volumeLong = CreateParameter("Volume Long (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _volumeShort = CreateParameter("Volume Short (%)", 2.5m, 0.1m, 50m, 0.1m, "Base");
            _minVolumeTester = CreateParameter("Min LOT (Tester)", 0m, 0m, 1000m, 0.01m, "Base");
            _slippagePercent = CreateParameter("Slippage (%)", 0.1m, 0.01m, 2m, 0.01m, "Base");
            _feePercent = CreateParameter("Fee (%)", 0.1m, 0.01m, 1m, 0.01m, "Base");
            _bondDaysToMaturity = CreateParameter("Bond days to maturity", 30, 1, 365, 1, "Base");

            // ── Стоп / Тейк ──────────────────────────────────────────────────────────
            //
            //   Если вы хотите динамический стоп (от индикатора / свинга) — задавайте
            //   цену прямо в LogicOpenPosition и не используйте эти параметры.
            //   Если хотите фиксированный % — используйте параметры ниже.
            //
            _stopPercent = CreateParameter("Stop (%)", 1.0m, 0.1m, 20m, 0.1m, "Exit");
            _profitPercent = CreateParameter("Profit (%, 0=off)", 0m, 0m, 100m, 0.5m, "Exit");
            _useTrailingStop = CreateParameter("Use Trailing Stop", false, "Exit");

            // TODO: создать параметры индикаторов здесь
            // _lengthMyIndicator = CreateParameter("Length", 20, 5, 200, 5, "Indicator");

            // TODO: создать и подключить индикаторы здесь
            // _myIndicator = IndicatorsFactory.CreateIndicatorByName("MyIndicator", name + "MyIndicator", false);
            // _myIndicator = (Aindicator)_tab.CreateCandleIndicator(_myIndicator, "Prime");
            // ((IndicatorParameterInt)_myIndicator.Parameters[0]).ValueInt = _lengthMyIndicator.ValueInt;
            // _myIndicator.Save();

            // ── Подписки на события BotTabSimple ─────────────────────────────────────
            ParametrsChangeByUser += OnParametrsChangeByUser;

            // Основной поток торговли
            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;       // закрытая свеча
            _tab.CandleUpdateEvent += _tab_CandleUpdateEvent;           // тиковое обновление текущей свечи (опционально)

            // Результаты ордеров открытия
            _tab.PositionOpeningSuccesEvent += OnPositionOpeningSucces;  // открыта: выставить стоп/тейк
            _tab.PositionOpeningFailEvent += OnPositionOpeningFail;      // отклонена: очистить словарь

            // Результаты ордеров закрытия
            _tab.PositionClosingSuccesEvent += OnPositionClosingSucces;  // позиция закрыта
            _tab.PositionClosingFailEvent += OnPositionClosingFail;      // ордер закрытия не прошёл

            // Срабатывание стоп-заявок
            _tab.PositionStopActivateEvent += OnPositionStopActivate;           // CloseAtStop активирован
            _tab.PositionProfitActivateEvent += OnPositionProfitActivate;       // CloseAtProfit активирован
            _tab.PositionBuyAtStopActivateEvent += OnPositionBuyAtStopActivate; // BuyAtStop активирован
            _tab.PositionSellAtStopActivateEvent += OnPositionSellAtStopActivate; // SellAtStop активирован

            // Изменение объёма позиции (частичные сделки)
            _tab.PositionNetVolumeChangeEvent += OnPositionNetVolumeChange;

            // Рыночные данные
            _tab.NewTickEvent += _tab_NewTickEvent;                       // тик (осторожно: высокая частота)
            _tab.BestBidAskChangeEvent += _tab_BestBidAskChangeEvent;     // лучший бид/аск
            _tab.MarketDepthUpdateEvent += _tab_MarketDepthUpdateEvent;   // стакан
            _tab.ServerTimeChangeEvent += _tab_ServerTimeChangeEvent;     // время сервера
            _tab.FirstTickToDayEvent += _tab_FirstTickToDayEvent;         // первый тик нового дня
            _tab.PortfolioOnExchangeChangedEvent += _tab_PortfolioOnExchangeChangedEvent; // изменение портфеля

            // Технические события
            _tab.SecuritySubscribeEvent += _tab_SecuritySubscribeEvent;   // подписка на инструмент
            _tab.MyTradeEvent += _tab_MyTradeEvent;                       // своя сделка
            _tab.OrderUpdateEvent += _tab_OrderUpdateEvent;               // обновление ордера
            _tab.CancelOrderFailEvent += _tab_CancelOrderFailEvent;       // не удалось отменить ордер
            _tab.IndicatorUpdateEvent += _tab_IndicatorUpdateEvent;       // обновление индикатора

            SyncParams();

            Description = "TODO: описание робота. " +
                          "Volume is calculated as % of deposit risk per trade.";
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ОБЯЗАТЕЛЬНЫЕ ПЕРЕГРУЗКИ
        // ════════════════════════════════════════════════════════════════════════════

        public override string GetNameStrategyType() => "TemplateRobotAdvanced";

        public override void ShowIndividualSettingsDialog() { }

        // ════════════════════════════════════════════════════════════════════════════
        //   СИНХРОНИЗАЦИЯ ПАРАМЕТРОВ
        // ════════════════════════════════════════════════════════════════════════════

        private void OnParametrsChangeByUser()
        {
            // TODO: обновить параметры индикаторов здесь
            // ((IndicatorParameterInt)_myIndicator.Parameters[0]).ValueInt = _lengthMyIndicator.ValueInt;
            // _myIndicator.Save();
            // _myIndicator.Reload();

            SyncParams();
        }

        private void SyncParams()
        {
            _curVolumeLong = _volumeLong.ValueDecimal;
            _curVolumeShort = _volumeShort.ValueDecimal;
            _curMinVolumeTester = _minVolumeTester.ValueDecimal;
            _curSlippagePercent = _slippagePercent.ValueDecimal;
            _curFeePercent = _feePercent.ValueDecimal;
            _curTimeZoneUtc = _timeZoneUtc.ValueInt;
            _curBondDaysToMaturity = _bondDaysToMaturity.ValueInt;
            _curStopPercent = _stopPercent.ValueDecimal;
            _curProfitPercent = _profitPercent.ValueDecimal;
            _curUseTrailingStop = _useTrailingStop.ValueBool;
        }

        private DateTime LocalTime(DateTime utcTime)
        {
            if (utcTime == DateTime.MinValue) return utcTime;
            return utcTime.AddHours(_curTimeZoneUtc);
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   СОБЫТИЯ РЫНОЧНЫХ ДАННЫХ
        //
        //   Большинство этих обработчиков намеренно оставлены пустыми — они нужны
        //   только если стратегия требует реакции на соответствующие события.
        //   Удалите ненужные подписки из конструктора, чтобы не тратить ресурсы.
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Обновление текущей (незакрытой) свечи по тику.
        /// Используйте, если стратегия должна реагировать внутри бара.
        /// Осторожно: вызывается очень часто. Тяжёлые вычисления делать здесь не стоит.
        /// </summary>
        private void _tab_CandleUpdateEvent(List<Candle> candles)
        {
            // TODO: логика на незакрытой свече (например, трейлинг стоп по цене)
        }

        /// <summary>
        /// Каждый тик (сделка на бирже). Высокая частота — используйте осторожно.
        /// </summary>
        private void _tab_NewTickEvent(Trade tick)
        {
            // TODO: логика по тикам
        }

        /// <summary>
        /// Изменение лучшего бида и аска.
        /// </summary>
        private void _tab_BestBidAskChangeEvent(decimal bestBid, decimal bestAsk)
        {
            // TODO: логика по изменению спреда
        }

        /// <summary>
        /// Обновление стакана заявок.
        /// </summary>
        private void _tab_MarketDepthUpdateEvent(MarketDepth marketDepth)
        {
            // TODO: логика по стакану
        }

        /// <summary>
        /// Изменение серверного времени.
        /// </summary>
        private void _tab_ServerTimeChangeEvent(DateTime serverTime)
        {
            // TODO: логика по времени (например, закрытие позиции в конце дня)
        }

        /// <summary>
        /// Первый тик нового торгового дня. Удобно для сброса дневных счётчиков.
        /// </summary>
        private void _tab_FirstTickToDayEvent(Trade firstTick)
        {
            // TODO: сброс дневных переменных / логика начала дня
        }

        /// <summary>
        /// Изменение портфеля на бирже (баланс, маржа и т.д.).
        /// </summary>
        private void _tab_PortfolioOnExchangeChangedEvent(Portfolio portfolio)
        {
            // TODO: логика при изменении портфеля
        }

        /// <summary>
        /// Подписка на инструмент подтверждена.
        /// </summary>
        private void _tab_SecuritySubscribeEvent(Security security)
        {
            // TODO: логика при подписке на инструмент
        }

        /// <summary>
        /// Обновление ордера (Placed, Active, Done, Cancel...).
        /// </summary>
        private void _tab_OrderUpdateEvent(Order order)
        {
            // TODO: дополнительная реакция на смену статуса ордера
        }

        /// <summary>
        /// Не удалось отменить ордер (биржа отказала).
        /// </summary>
        private void _tab_CancelOrderFailEvent(Order order)
        {
            SendNewLogMessage($"[ORDER] Не удалось отменить ордер #{order.NumberUser}", LogMessageType.Error);
        }

        /// <summary>
        /// Наша сделка прошла (частичное или полное исполнение ордера).
        /// </summary>
        private void _tab_MyTradeEvent(MyTrade myTrade)
        {
            // TODO: логика при исполнении нашей сделки
        }

        /// <summary>
        /// Индикатор пересчитан (если нужна реакция на обновление индикатора).
        /// </summary>
        private void _tab_IndicatorUpdateEvent()
        {
            // TODO: логика при обновлении индикаторов
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ГЛАВНЫЙ ОБРАБОТЧИК СВЕЧИ
        // ════════════════════════════════════════════════════════════════════════════

        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
            if (_regime.ValueString == "Off") return;

            // TODO: заменить минимальное количество свечей под свой индикатор
            if (candles.Count < 20) return;

            DateTime now = LocalTime(_tab.TimeServerCurrent);
            if (!_tradePeriods.CanTradeThisTime(now))
            {
                if (!_nonTradePeriodLogged)
                {
                    _nonTradePeriodLogged = true;
                    SendNewLogMessage($"⏰ Неторговое время (UTC+{_curTimeZoneUtc}): {now:HH:mm:ss} — входы заблокированы", LogMessageType.System);
                }
                return;
            }

            if (_nonTradePeriodLogged)
            {
                _nonTradePeriodLogged = false;
                SendNewLogMessage($"✅ Торговое время возобновлено (UTC+{_curTimeZoneUtc}): {now:HH:mm:ss}", LogMessageType.System);
            }

            List<Position> openPositions = _tab.PositionsOpenAll;

            if (openPositions != null && openPositions.Count != 0)
                LogicClosePosition(candles);

            if (_regime.ValueString == "CLOSE-POS") return;

            if (openPositions == null || openPositions.Count == 0)
                LogicOpenPosition(candles);
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ЛОГИКА ОТКРЫТИЯ — TODO: реализовать
        //
        //   Шаблон показывает правильный порядок действий:
        //   1. Получить сигнальные значения индикаторов
        //   2. Определить цену стопа (фиксированный % или динамически от индикатора)
        //   3. Рассчитать объём через CalcVolume(side, entry, stopPrice)
        //   4. Отправить ордер и сохранить {стоп, тейк} в словарь _stopByOrderId
        // ════════════════════════════════════════════════════════════════════════════

        private void LogicOpenPosition(List<Candle> candles)
        {
            decimal lastPrice = candles[candles.Count - 1].Close;

            // TODO: получить значения индикатора
            // decimal signalValue = _myIndicator.DataSeries[0].Last;

            // ── LONG ─────────────────────────────────────────────────────────────────
            if (_regime.ValueString != "SHORT-POS")
            {
                // TODO: условие входа в лонг
                // if (lastPrice > signalValue)
                // {
                //     decimal entry = _tab.PriceBestAsk;
                //     if (entry <= 0) entry = lastPrice;
                //
                //     // Вариант 1: фиксированный % стоп из параметров
                //     decimal stopPrice = entry * (1m - _curStopPercent / 100m);
                //
                //     // Вариант 2: динамический стоп от индикатора / свинга:
                //     // decimal stopPrice = /* ваш уровень */;
                //
                //     if (stopPrice <= 0 || stopPrice >= entry) return;
                //
                //     decimal volume = CalcVolume(Side.Buy, entry, stopPrice);
                //     if (volume <= 0) return;
                //
                //     decimal slippage = entry * (_curSlippagePercent / 100m);
                //     Position pos = _tab.BuyAtLimit(volume, entry + slippage);
                //     // или:  _tab.BuyAtMarket(volume);
                //     // или:  _tab.BuyAtStop(volume, entry+slippage, entry, StopActivateType.HigherOrEqual, 3);
                //
                //     if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                //     {
                //         SendNewLogMessage("[OPEN] BUY — позиция не создана или OpenOrders пуст", LogMessageType.Error);
                //         return;
                //     }
                //
                //     // Тейк: 0 = не выставлять
                //     decimal profitPrice = _curProfitPercent > 0
                //         ? entry * (1m + _curProfitPercent / 100m)
                //         : 0m;
                //
                //     _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser,
                //         new StopProfitPair { StopPrice = stopPrice, ProfitPrice = profitPrice });
                //
                //     SendNewLogMessage(
                //         $"[OPEN] BUY | entry≈{entry:F4} stop={stopPrice:F4} profit={profitPrice:F4} vol={volume}",
                //         LogMessageType.System);
                // }
            }

            // ── SHORT ────────────────────────────────────────────────────────────────
            if (_regime.ValueString != "LONG-POS")
            {
                // TODO: условие входа в шорт
                // if (lastPrice < signalValue)
                // {
                //     decimal entry = _tab.PriceBestBid;
                //     if (entry <= 0) entry = lastPrice;
                //
                //     decimal stopPrice = entry * (1m + _curStopPercent / 100m);
                //     if (stopPrice <= 0 || stopPrice <= entry) return;
                //
                //     decimal volume = CalcVolume(Side.Sell, entry, stopPrice);
                //     if (volume <= 0) return;
                //
                //     decimal slippage = entry * (_curSlippagePercent / 100m);
                //     Position pos = _tab.SellAtLimit(volume, entry - slippage);
                //
                //     if (pos == null || pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                //     {
                //         SendNewLogMessage("[OPEN] SELL — позиция не создана или OpenOrders пуст", LogMessageType.Error);
                //         return;
                //     }
                //
                //     decimal profitPrice = _curProfitPercent > 0
                //         ? entry * (1m - _curProfitPercent / 100m)
                //         : 0m;
                //
                //     _stopByOrderId.TryAdd(pos.OpenOrders[0].NumberUser,
                //         new StopProfitPair { StopPrice = stopPrice, ProfitPrice = profitPrice });
                //
                //     SendNewLogMessage(
                //         $"[OPEN] SELL | entry≈{entry:F4} stop={stopPrice:F4} profit={profitPrice:F4} vol={volume}",
                //         LogMessageType.System);
                // }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   ЛОГИКА ЗАКРЫТИЯ — TODO: реализовать
        //
        //   Начальный стоп и тейк выставляются автоматически в OnPositionOpeningSucces.
        //   Здесь — дополнительные условия выхода по сигналу индикатора,
        //   а также обновление трейлинг-стопа каждый бар.
        // ════════════════════════════════════════════════════════════════════════════

        private void LogicClosePosition(List<Candle> candles)
        {
            List<Position> openPositions = _tab.PositionsOpenAll;

            for (int i = 0; openPositions != null && i < openPositions.Count; i++)
            {
                Position pos = openPositions[i];

                // Пропускаем все кроме полностью открытых
                if (pos.State != PositionStateType.Open) continue;

                // Пропускаем если уже висит активный ордер на закрытие
                if (pos.CloseActive) continue;

                // ── Справочник: свойства Position, полезные для логики выхода ───────
                //
                //   pos.OpenVolume               — текущий открытый объём → передавать в CloseAt*
                //   pos.MaxVolume                — объём при открытии
                //   pos.EntryPrice               — средняя цена входа (по сделкам)
                //   pos.Direction                — Side.Buy / Side.Sell
                //   pos.ProfitPortfolioPercent   — PnL в % от портфеля (с комиссией)  ← правильное имя
                //   pos.ProfitPortfolioAbs       — PnL в деньгах (с комиссией и шагом цены)
                //   pos.ProfitOperationPercent   — PnL в % от цены входа (без комиссии)
                //   pos.ProfitOperationAbs       — PnL в пунктах (без комиссии)
                //   pos.StopOrderRedLine         — текущий уровень стопа (> 0 если выставлен)
                //   pos.ProfitOrderRedLine       — текущий уровень тейка (> 0 если выставлен)
                //   pos.StopOrderIsActive        — стоп активен
                //   pos.ProfitOrderIsActive      — тейк активен
                //   pos.TimeOpen                 — время открытия позиции
                //   pos.Comment                  — произвольное поле для ваших данных
                //   pos.SignalTypeOpen           — метка сигнала открытия
                //   pos.Number                   — уникальный номер позиции

                // ── Трейлинг стоп ───────────────────────────────────────────────────
                //
                //   CloseAtTrailingStop двигает стоп только в прибыльную сторону.
                //   Вызывайте каждый бар — движок сам решит, нужно ли обновлять.
                //
                if (_curUseTrailingStop)
                {
                    // TODO: задать уровень трейлинга (пример: по Low/High последней свечи)
                    // decimal lastLow  = candles[candles.Count - 1].Low;
                    // decimal lastHigh = candles[candles.Count - 1].High;
                    // decimal slippage = candles[candles.Count - 1].Close * (_curSlippagePercent / 100m);
                    //
                    // if (pos.Direction == Side.Buy)
                    //     _tab.CloseAtTrailingStop(pos, lastLow, lastLow - slippage);
                    // else
                    //     _tab.CloseAtTrailingStop(pos, lastHigh, lastHigh + slippage);
                }

                // ── Выход по сигналу индикатора ─────────────────────────────────────
                //
                // TODO: добавить условия выхода по сигналу
                //
                // decimal signalValue = _myIndicator.DataSeries[0].Last;
                // decimal slippage    = pos.EntryPrice * (_curSlippagePercent / 100m);
                //
                // if (pos.Direction == Side.Buy && /* сигнал разворота */)
                //     _tab.CloseAtMarket(pos, pos.OpenVolume, "SignalClose");
                //
                // if (pos.Direction == Side.Sell && /* сигнал разворота */)
                //     _tab.CloseAtMarket(pos, pos.OpenVolume, "SignalClose");

                // ── Аварийный выход по просадке (опционально) ──────────────────────
                //
                // Пример: выход если просадка позиции превышает 1.5× стоп-параметр
                // if (pos.ProfitPortfolioPercent < -(_curStopPercent * 1.5m))
                // {
                //     SendNewLogMessage(
                //         $"[EMERGENCY] pos#{pos.Number} PnL={pos.ProfitPortfolioPercent:F2}% — аварийный выход",
                //         LogMessageType.Error);
                //     _tab.CloseAtMarket(pos, pos.OpenVolume, "EmergencyStop");
                // }
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   СОБЫТИЯ ПОЗИЦИИ
        // ════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Позиция открыта (биржа подтвердила исполнение).
        /// Выставляем начальный стоп и тейк.
        /// Вызывается АСИНХРОННО — ConcurrentDictionary обеспечивает потокобезопасность.
        /// </summary>
        private void OnPositionOpeningSucces(Position pos)
        {
            // Защита от двойного срабатывания
            if (pos.StopOrderRedLine > 0)
            {
                SendNewLogMessage($"[STOP] Стоп уже выставлен для pos#{pos.Number}", LogMessageType.System);
                return;
            }

            if (pos.OpenOrders == null || pos.OpenOrders.Count == 0)
            {
                SendNewLogMessage($"[STOP] OpenOrders пуст для pos#{pos.Number} — стоп не выставлен", LogMessageType.Error);
                return;
            }

            int orderKey = pos.OpenOrders[0].NumberUser;

            if (!_stopByOrderId.TryRemove(orderKey, out StopProfitPair pair) || pair.StopPrice <= 0)
            {
                SendNewLogMessage($"[STOP] Стоп не найден для orderKey={orderKey} pos#{pos.Number}", LogMessageType.Error);
                return;
            }

            // Ключ удалён сразу при TryRemove — не накапливается

            decimal slippage = pair.StopPrice * (_curSlippagePercent / 100m);

            // ── Стоп ────────────────────────────────────────────────────────────────
            if (pos.Direction == Side.Buy)
                _tab.CloseAtStop(pos, pair.StopPrice, pair.StopPrice - slippage);
            else
                _tab.CloseAtStop(pos, pair.StopPrice, pair.StopPrice + slippage);

            // ── Тейк ────────────────────────────────────────────────────────────────
            if (pair.ProfitPrice > 0)
            {
                decimal profitSlippage = pair.ProfitPrice * (_curSlippagePercent / 100m);

                if (pos.Direction == Side.Buy)
                    _tab.CloseAtProfit(pos, pair.ProfitPrice, pair.ProfitPrice - profitSlippage);
                else
                    _tab.CloseAtProfit(pos, pair.ProfitPrice, pair.ProfitPrice + profitSlippage);
            }

            SendNewLogMessage(
                $"[STOP] pos#{pos.Number} {pos.Direction} | stop={pair.StopPrice:F4} profit={pair.ProfitPrice:F4} orderKey={orderKey}",
                LogMessageType.System);
        }

        /// <summary>
        /// Ордер открытия отклонён биржей или отменён до исполнения.
        /// Удаляем запись из словаря, иначе она останется навсегда.
        /// </summary>
        private void OnPositionOpeningFail(Position pos)
        {
            if (pos.OpenOrders == null || pos.OpenOrders.Count == 0)
                return;

            int orderKey = pos.OpenOrders[0].NumberUser;

            if (_stopByOrderId.TryRemove(orderKey, out _))
            {
                SendNewLogMessage(
                    $"[STOP] Стоп удалён из словаря (OpeningFail) orderKey={orderKey} pos#{pos.Number}",
                    LogMessageType.System);
            }
        }

        /// <summary>
        /// Позиция успешно закрыта.
        /// </summary>
        private void OnPositionClosingSucces(Position pos)
        {
            SendNewLogMessage(
                $"[CLOSE] ✅ pos#{pos.Number} {pos.Direction} закрыта | PnL={pos.ProfitPortfolioPercent:F2}% ({pos.ProfitPortfolioAbs:F4})",
                LogMessageType.System);

            // TODO: логика после закрытия (сброс счётчиков, запись статистики и т.д.)
        }

        /// <summary>
        /// Ордер закрытия не прошёл (биржа отказала).
        /// Решаем, что делать: повторить, перевыставить, или оставить открытой.
        /// </summary>
        private void OnPositionClosingFail(Position pos)
        {
            SendNewLogMessage(
                $"[CLOSE] ❌ Ошибка закрытия pos#{pos.Number} {pos.Direction} — ордер не прошёл",
                LogMessageType.Error);

            // TODO: повторная попытка закрыть позицию:
            // if (pos.OpenVolume > 0)
            //     _tab.CloseAtMarket(pos, pos.OpenVolume, "ClosingFailRetry");
        }

        /// <summary>
        /// CloseAtStop сработал (цена пробила уровень стопа).
        /// </summary>
        private void OnPositionStopActivate(Position pos)
        {
            SendNewLogMessage(
                $"[STOP] 🔴 Stop activated pos#{pos.Number} {pos.Direction} | RedLine={pos.StopOrderRedLine:F4}",
                LogMessageType.System);
        }

        /// <summary>
        /// CloseAtProfit сработал (цена достигла уровня тейка).
        /// </summary>
        private void OnPositionProfitActivate(Position pos)
        {
            SendNewLogMessage(
                $"[PROFIT] 🟢 Profit activated pos#{pos.Number} {pos.Direction} | ProfitLine={pos.ProfitOrderRedLine:F4}",
                LogMessageType.System);
        }

        /// <summary>
        /// BuyAtStop — цена активировала стоп-заявку на покупку.
        /// </summary>
        private void OnPositionBuyAtStopActivate(Position pos)
        {
            SendNewLogMessage(
                $"[STOP-OPEN] BuyAtStop активирован pos#{pos.Number}",
                LogMessageType.System);
        }

        /// <summary>
        /// SellAtStop — цена активировала стоп-заявку на продажу.
        /// </summary>
        private void OnPositionSellAtStopActivate(Position pos)
        {
            SendNewLogMessage(
                $"[STOP-OPEN] SellAtStop активирован pos#{pos.Number}",
                LogMessageType.System);
        }

        /// <summary>
        /// Изменился чистый объём позиции (частичное исполнение открывающего/закрывающего ордера).
        /// </summary>
        private void OnPositionNetVolumeChange(Position pos)
        {
            // TODO: реакция на частичное исполнение (pyramid, усреднение и т.д.)
        }

        // ════════════════════════════════════════════════════════════════════════════
        //   РАСЧЁТ ОБЪЁМА
        //
        //   Объём рассчитывается как риск (% депозита) / (% расстояния до стопа
        //   + проскальзывание + двойная комиссия).
        //   Поддерживаются секции: SPOT/LinearPerpetual, Stocks MOEX,
        //   InversFutures, Futures MOEX.
        //
        //   Вызывать: decimal volume = CalcVolume(Side.Buy, entryPrice, stopPrice);
        // ════════════════════════════════════════════════════════════════════════════

        private decimal CalcVolume(Side side, decimal entryPrice, decimal stopPrice)
        {
            if (_tab?.Security == null || _tab.Security.Lot <= 0) return 0;
            if (entryPrice <= 0 || stopPrice <= 0) return 0;
            decimal stopPercent = Math.Abs(entryPrice - stopPrice) / entryPrice * 100m;
            return GetVolume(side, entryPrice, stopPrice, stopPercent);
        }

        // Контекст одного вызова GetVolume: накапливает промежуточные значения для лога.
        private struct VolumeCalcCtx
        {
            public Side Side;
            public decimal EntryPrice;
            public decimal StopPrice;
            public decimal StopPercent;
            public decimal Balance;
            public decimal RiskPct;
            public decimal RiskMoney;
            public decimal RealStopPct;
            public decimal PosSize;
            public decimal Volume;
            public Security Sec;
            public string RejectReason;

            // ── Промежуточные значения для лога ─────────────────────────────────────
            // Объём до применения VolumeStep
            public decimal VolumeRaw;

            // SPOT/LinearPerpetual (UsePriceStepCostToCalculateVolume)
            public decimal ContractCost;        // entryPrice / PriceStep * PriceStepCost

            // Bonds MOEX
            public decimal BondPrice;           // NominalCurrent * entryPrice / 100

            // InversFutures
            public decimal PosSizeInverse;      // Balance * entryPrice * RiskPct% / RealStopPct

            // Futures MOEX
            public decimal StopPts;             // |entry - stop| в пунктах
            public decimal LossPerContract;     // StopPts / PriceStep * PriceStepCost
            public decimal VolByRisk;           // RiskMoney / LossPerContract
            public decimal VolByMargin;         // Balance / margin
            public decimal MarginUsed;          // MarginBuy или MarginSell (в зависимости от Side)
            public string LimitingFactor;       // "RISK" или "MARGIN"

            // Минимальный объём (resolved с учётом типа C_Currency/Contract)
            public decimal MinVolumeResolved;

            // Лимиты цены инструмента
            public decimal PriceLimitLow;
            public decimal PriceLimitHigh;

            // Портфель
            public decimal PortfolioValueCurrent;
            public decimal PortfolioValueBlocked;
            public decimal PortfolioUnrealizedPnl;
            public string PortfolioNumber;
        }

        private decimal GetVolume(Side side, decimal entryPrice, decimal stopPrice, decimal stopPercent)
        {
            VolumeCalcCtx ctx = new VolumeCalcCtx
            {
                Side = side,
                EntryPrice = entryPrice,
                StopPrice = stopPrice,
                StopPercent = stopPercent,
                RejectReason = "ok",
            };

            if (stopPercent <= 0) return Reject(ref ctx, "stopPercent <= 0");
            if (entryPrice <= 0) return Reject(ref ctx, "entryPrice <= 0");
            if (stopPrice <= 0 && _modeTrade.ValueString == "Futures MOEX")
                return Reject(ref ctx, "stopPrice <= 0");

            ctx.Balance = GetAssetValue(_tab.Portfolio, _assetNameCurrent.ValueString);
            if (ctx.Balance <= 0) return Reject(ref ctx, "balance <= 0");

            // Сохраняем данные портфеля для лога
            if (_tab.Portfolio != null)
            {
                ctx.PortfolioNumber = _tab.Portfolio.Number;
                ctx.PortfolioValueCurrent = _tab.Portfolio.ValueCurrent;
                ctx.PortfolioValueBlocked = _tab.Portfolio.ValueBlocked;
                ctx.PortfolioUnrealizedPnl = _tab.Portfolio.UnrealizedPnl;
            }

            ctx.RealStopPct = stopPercent / 100m
                            + _curSlippagePercent / 100m
                            + _curFeePercent / 100m * 2m;
            if (ctx.RealStopPct <= 0) return Reject(ref ctx, "realStopPct <= 0");

            ctx.RiskPct = side == Side.Buy ? _curVolumeLong : _curVolumeShort;
            ctx.RiskMoney = ctx.Balance * (ctx.RiskPct / 100m);
            ctx.PosSize = ctx.RiskMoney / ctx.RealStopPct;

            ctx.Sec = _tab.Security;
            if (ctx.Sec == null) return Reject(ref ctx, "sec == null");

            // Сохраняем лимиты цены для лога (доступны только после ctx.Sec)
            ctx.PriceLimitLow = ctx.Sec.PriceLimitLow;
            ctx.PriceLimitHigh = ctx.Sec.PriceLimitHigh;

            if (StartProgram != StartProgram.IsOsOptimizer &&
                StartProgram != StartProgram.IsTester)
            {
                if (ctx.Sec.State != SecurityStateType.Activ)
                    return Reject(ref ctx, $"state not active ({ctx.Sec.State})");

                if (ctx.Sec.PriceLimitHigh > 0 && entryPrice > ctx.Sec.PriceLimitHigh)
                    return Reject(ref ctx, $"entryPrice {entryPrice} > PriceLimitHigh {ctx.Sec.PriceLimitHigh}");

                if (ctx.Sec.PriceLimitLow > 0 && entryPrice < ctx.Sec.PriceLimitLow)
                    return Reject(ref ctx, $"entryPrice {entryPrice} < PriceLimitLow {ctx.Sec.PriceLimitLow}");

                if ((ctx.Sec.SecurityType == SecurityType.Futures || ctx.Sec.SecurityType == SecurityType.Option) &&
                    ctx.Sec.Expiration.Year > 1970 &&
                    ctx.Sec.Expiration < DateTime.Now)
                    return Reject(ref ctx, $"instrument expired (Expiration={ctx.Sec.Expiration:yyyy-MM-dd})");

                if (ctx.Sec.SecurityType == SecurityType.Bond &&
                    ctx.Sec.MaturityDate != DateTime.MinValue &&
                    ctx.Sec.MaturityDate < DateTime.Now.AddDays(_curBondDaysToMaturity))
                    return Reject(ref ctx, $"bond maturity too close ({ctx.Sec.MaturityDate:yyyy-MM-dd})");
            }

            decimal mult = ctx.Sec.DecimalsVolume > 0 ? (decimal)Math.Pow(10, ctx.Sec.DecimalsVolume) : 1m;

            switch (_modeTrade.ValueString)
            {
                case "SPOT и LinearPerpetual":
                    if (ctx.Sec.SecurityType != SecurityType.CurrencyPair &&
                        ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for SPOT ({ctx.Sec.SecurityType})");

                    if (ctx.Sec.UsePriceStepCostToCalculateVolume && ctx.Sec.PriceStep > 0 && ctx.Sec.PriceStepCost > 0)
                    {
                        decimal contractCost = entryPrice / ctx.Sec.PriceStep * ctx.Sec.PriceStepCost;
                        ctx.ContractCost = contractCost;
                        if (contractCost <= 0) return Reject(ref ctx, "contractCost <= 0");
                        ctx.Volume = Math.Floor(ctx.PosSize / contractCost * mult) / mult;
                    }
                    else
                    {
                        ctx.Volume = Math.Floor(ctx.PosSize / entryPrice * mult) / mult;
                    }
                    break;

                case "Stocks MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Stock &&
                        ctx.Sec.SecurityType != SecurityType.Fund &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for Stocks ({ctx.Sec.SecurityType})");
                    if (ctx.Sec.Lot <= 0) return Reject(ref ctx, "Lot <= 0");

                    ctx.Volume = Math.Floor(ctx.PosSize / entryPrice / ctx.Sec.Lot * mult) / mult;
                    break;

                case "Bonds MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Bond &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for Bonds ({ctx.Sec.SecurityType})");
                    if (ctx.Sec.Lot <= 0 || ctx.Sec.NominalCurrent <= 0)
                        return Reject(ref ctx, $"Lot={ctx.Sec.Lot} or NominalCurrent={ctx.Sec.NominalCurrent} <= 0");

                    decimal bondPrice = ctx.Sec.NominalCurrent * entryPrice / 100m;
                    ctx.BondPrice = bondPrice;
                    if (bondPrice <= 0) return Reject(ref ctx, "bondPrice <= 0");
                    ctx.Volume = Math.Floor(ctx.PosSize / bondPrice / ctx.Sec.Lot * mult) / mult;
                    break;

                case "InversFutures":
                    if (ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for InversFutures ({ctx.Sec.SecurityType})");
                    if (ctx.Sec.Lot <= 0) return Reject(ref ctx, "Lot <= 0");

                    string selectedAsset = _assetNameCurrent.ValueString.ToUpper();
                    bool isUsdAsset = selectedAsset == "USDT" || selectedAsset == "USDC" ||
                                      selectedAsset == "USD" || selectedAsset == "RUB" ||
                                      selectedAsset == "EUR" || selectedAsset == "PRIME";
                    if (isUsdAsset)
                        return Reject(ref ctx, $"asset '{selectedAsset}' is USD/fiat — укажите базовый крипто-актив (BTC/ETH/...)");

                    decimal posSizeInverse = ctx.Balance * entryPrice * (ctx.RiskPct / 100m) / ctx.RealStopPct;
                    ctx.PosSizeInverse = posSizeInverse;
                    ctx.Volume = Math.Floor(posSizeInverse / ctx.Sec.Lot * mult) / mult;
                    break;

                case "Futures MOEX":
                    if (ctx.Sec.SecurityType != SecurityType.Futures &&
                        ctx.Sec.SecurityType != SecurityType.Option &&
                        ctx.Sec.SecurityType != SecurityType.None)
                        return Reject(ref ctx, $"wrong secType for FuturesMOEX ({ctx.Sec.SecurityType})");
                    if (_assetNameCurrent.ValueString.Equals("Prime", StringComparison.OrdinalIgnoreCase))
                        return Reject(ref ctx, "asset 'Prime' недопустим для Futures MOEX — укажите 'RUB'");
                    if (ctx.Sec.PriceStep <= 0 || ctx.Sec.PriceStepCost <= 0)
                        return Reject(ref ctx, $"PriceStep={ctx.Sec.PriceStep} PriceStepCost={ctx.Sec.PriceStepCost}");

                    decimal margin = side == Side.Buy ? ctx.Sec.MarginBuy : ctx.Sec.MarginSell;
                    if (margin <= 0)
                        return Reject(ref ctx, $"margin <= 0 (MarginBuy={ctx.Sec.MarginBuy} MarginSell={ctx.Sec.MarginSell})");

                    decimal stopPts = Math.Abs(entryPrice - stopPrice);
                    decimal lossPerContract = stopPts / ctx.Sec.PriceStep * ctx.Sec.PriceStepCost;
                    if (lossPerContract <= 0) return Reject(ref ctx, "lossPerContract <= 0");

                    decimal byRisk = Math.Floor(ctx.RiskMoney / lossPerContract);
                    decimal byGo = Math.Floor(ctx.Balance / margin);
                    ctx.Volume = Math.Min(byRisk, byGo);

                    ctx.MarginUsed = margin;
                    ctx.StopPts = stopPts;
                    ctx.LossPerContract = lossPerContract;
                    ctx.VolByRisk = byRisk;
                    ctx.VolByMargin = byGo;
                    ctx.LimitingFactor = byRisk <= byGo ? "RISK" : "MARGIN";
                    break;

                default:
                    return Reject(ref ctx, $"unknown mode '{_modeTrade.ValueString}'");
            }

            if (ctx.Volume <= 0) return Reject(ref ctx, "volume <= 0 after calculation");

            ctx.VolumeRaw = ctx.Volume;
            if (ctx.Sec.VolumeStep > 0)
                ctx.Volume = Math.Floor(ctx.Volume / ctx.Sec.VolumeStep) * ctx.Sec.VolumeStep;

            if (StartProgram == StartProgram.IsOsOptimizer ||
                StartProgram == StartProgram.IsTester)
            {
                if (_curMinVolumeTester > 0 && ctx.Volume < _curMinVolumeTester)
                    return Reject(ref ctx, $"volume={ctx.Volume} < minVolumeTester={_curMinVolumeTester}");
            }
            else
            {
                if (ctx.Sec.MinTradeAmount > 0)
                {
                    decimal minVolume = ctx.Sec.MinTradeAmountType == MinTradeAmountType.C_Currency
                        ? ctx.Sec.MinTradeAmount / entryPrice
                        : ctx.Sec.MinTradeAmount;

                    ctx.MinVolumeResolved = minVolume;

                    if (ctx.Volume < minVolume)
                        return Reject(ref ctx, $"volume={ctx.Volume} < minVolume={minVolume} (MinTradeAmount={ctx.Sec.MinTradeAmount} type={ctx.Sec.MinTradeAmountType})");
                }
            }

            return LogVolume(ref ctx);
        }

        private decimal Reject(ref VolumeCalcCtx ctx, string reason)
        {
            ctx.Volume = 0;
            ctx.RejectReason = reason;
            return LogVolume(ref ctx);
        }

        private decimal LogVolume(ref VolumeCalcCtx ctx)
        {
            if (_tradeLogOnOff.ValueString == "On")
            {
                Security s = ctx.Sec;
                string log =
                $@" -GET VOLUME DEBUG
                SECURITY               = {s?.Name} | TYPE = {s?.SecurityType} | STATE = {s?.State}
                MODE                   = {_modeTrade.ValueString}
                SIDE                   = {ctx.Side}
                START PROGRAM          = {StartProgram}
                ------ PORTFOLIO ------
                PORTFOLIO NUMBER       = {ctx.PortfolioNumber}
                VALUE CURRENT          = {ctx.PortfolioValueCurrent:F2}
                VALUE BLOCKED          = {ctx.PortfolioValueBlocked:F2}
                UNREALIZED PNL         = {ctx.PortfolioUnrealizedPnl:F2}
                ------ ASSET / BALANCE ------
                ASSET                  = {_assetNameCurrent.ValueString}
                BALANCE                = {ctx.Balance:F6}
                ------ RISK ------
                RISK PCT               = {ctx.RiskPct:F4} %
                RISK MONEY             = {ctx.RiskMoney:F6}
                STOP %                 = {ctx.StopPercent:F6} %
                SLIPPAGE %             = {_curSlippagePercent:F4} %
                FEE %                  = {_curFeePercent:F4} %
                REAL STOP PCT          = {ctx.RealStopPct:F6}
                POS SIZE               = {ctx.PosSize:F6}
                ------ PRICE ------
                ENTRY PRICE            = {ctx.EntryPrice:F4}
                STOP PRICE             = {ctx.StopPrice:F4}
                PRICE LIMIT LOW        = {ctx.PriceLimitLow}
                PRICE LIMIT HIGH       = {ctx.PriceLimitHigh}
                ------ INSTRUMENT ------
                LOT                    = {s?.Lot}
                DECIMALS VOL           = {s?.DecimalsVolume}
                VOLUME STEP            = {s?.VolumeStep}
                MIN TRADE (LOT) AMOUNT = {s?.MinTradeAmount} ({s?.MinTradeAmountType})
                MIN VOLUME RESOLVED    = {(ctx.MinVolumeResolved != 0 ? ctx.MinVolumeResolved.ToString() : "n/a")}
                MIN (LOT) TESTER       = {_curMinVolumeTester}
                PRICE STEP             = {s?.PriceStep}
                STEP COST              = {s?.PriceStepCost}
                USE STEP COST CALC     = {s?.UsePriceStepCostToCalculateVolume}
                EXPIRATION             = {s?.Expiration:yyyy-MM-dd}
                MARGIN BUY             = {s?.MarginBuy}
                MARGIN SELL            = {s?.MarginSell}
                NOMINAL CURRENT        = {s?.NominalCurrent}
                MATURITY DATE          = {s?.MaturityDate:yyyy-MM-dd}
                ACI VALUE              = {s?.AciValue}
                ------ MODE INTERMEDIATES ------
                CONTRACT COST (SPOT)   = {(ctx.ContractCost != 0 ? ctx.ContractCost.ToString("F6") : "n/a")}
                BOND PRICE             = {(ctx.BondPrice != 0 ? ctx.BondPrice.ToString("F6") : "n/a")}
                POS SIZE INVERSE       = {(ctx.PosSizeInverse != 0 ? ctx.PosSizeInverse.ToString("F6") : "n/a")}
                STOP PTS               = {(ctx.StopPts != 0 ? ctx.StopPts.ToString("F6") : "n/a")}
                LOSS PER CONTRACT      = {(ctx.LossPerContract != 0 ? ctx.LossPerContract.ToString("F6") : "n/a")}
                MARGIN USED            = {(ctx.MarginUsed != 0 ? ctx.MarginUsed.ToString("F6") : "n/a")}
                VOL BY RISK            = {(ctx.VolByRisk != 0 ? ctx.VolByRisk.ToString() : "n/a")}
                VOL BY MARGIN          = {(ctx.VolByMargin != 0 ? ctx.VolByMargin.ToString() : "n/a")}
                LIMITING FACTOR        = {(ctx.LimitingFactor ?? "n/a")}
                ------ RESULT ------
                VOLUME RAW (pre-step)  = {(ctx.VolumeRaw != 0 ? ctx.VolumeRaw.ToString() : "n/a")}
                VOLUME                 = {ctx.Volume}
                REJECT REASON          = {ctx.RejectReason}
                -";

                SendNewLogMessage(log, Logging.LogMessageType.System);
            }

            return ctx.Volume;
        }

        private decimal GetAssetValue(Portfolio portfolio, string assetName)
        {
            if (portfolio == null) return 0;

            // Prime = суммарная стоимость портфеля в валюте счёта (деньги + открытые позиции).
            if (assetName == "Prime") return portfolio.ValueCurrent;

            List<PositionOnBoard> positions = portfolio.GetPositionOnBoard();
            if (positions == null) return 0;

            foreach (PositionOnBoard p in positions)
            {
                if (p.SecurityNameCode.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                    return p.ValueCurrent;
            }

            return 0;
        }
    }
}