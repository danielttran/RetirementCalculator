using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RetirementProtocol
{
    public class MarketData
    {
        public double ReturnPct { get; set; }
        public double DividendYield { get; set; }
        public double CurrentPrice { get; set; }
    }

    // --- STATE TAX CONFIGURATION ---
    public class StateTaxProfile
    {
        public string Name { get; set; }
        public bool HasIncomeTax { get; set; }
        public double FlatRate { get; set; } // For flat-tax states
        public bool IsProgressive { get; set; }
        public double SingleStdDeduction { get; set; }
        public double JointStdDeduction { get; set; }
        public double RetirementExclusion { get; set; } // Special retirement income exclusion
        public double SocialSecurityExemptPct { get; set; } // % of SS that's exempt (100 = fully exempt)
        public List<TaxBracket> Brackets { get; set; }
        public string Notes { get; set; }
    }

    public class TaxBracket
    {
        public double Threshold { get; set; }
        public double Rate { get; set; }
    }

    class Program
    {
        const string LogFileName = "transaction_log.csv";
        const string StockTicker = "FSKAX";
        const string CashTicker = "SPAXX";
        const double AnnualInflation = 0.03;
        const double TaxSafetyMargin = 0.15;
        const double MinDividendAlert = 100.00;
        const double MinCashMonthsWarning = 6.0;

        static readonly double[] HistoricalReturns = new double[] {
            0.438, -0.083, -0.251, -0.438, -0.086, 0.499, -0.011, 0.467, 0.319, -0.353,
            0.292, -0.011, -0.106, -0.127, 0.191, 0.250, 0.190, 0.358, -0.084, 0.052,
            0.057, 0.183, 0.308, 0.236, 0.181, -0.012, 0.525, 0.326, 0.064, -0.104,
            0.437, 0.120, 0.003, 0.266, -0.088, 0.226, 0.164, 0.124, -0.099, 0.238,
            0.108, -0.082, 0.035, 0.142, 0.187, -0.143, -0.259, 0.370, 0.238, -0.070,
            0.065, 0.185, 0.317, -0.047, 0.204, 0.223, 0.061, 0.312, 0.185, 0.057,
            0.165, 0.314, -0.032, 0.302, 0.074, 0.099, 0.013, 0.373, 0.226, 0.331,
            0.283, 0.208, -0.090, -0.118, -0.219, 0.283, 0.107, 0.048, 0.156, 0.055,
            -0.365, 0.259, 0.148, 0.021, 0.158, 0.321, 0.135, 0.013, 0.117, 0.216,
            -0.042, 0.312, 0.180, 0.284, -0.180, 0.260
        };

        static readonly Dictionary<int, double> RmdTable = new Dictionary<int, double>
        {
            {72, 27.4}, {73, 26.5}, {74, 25.5}, {75, 24.6}, {76, 23.7}, {77, 22.9}, {78, 22.0}, {79, 21.1},
            {80, 20.2}, {81, 19.4}, {82, 18.5}, {83, 17.7}, {84, 16.8}, {85, 16.0}, {86, 15.2}, {87, 14.4},
            {88, 13.7}, {89, 12.9}, {90, 12.2}, {91, 11.5}, {92, 10.8}, {93, 10.1}, {94, 9.5}, {95, 8.9},
            {96, 8.4}, {97, 7.8}, {98, 7.3}, {99, 6.8}, {100, 6.4}, {101, 6.0}, {102, 5.6}, {103, 5.2},
            {104, 4.9}, {105, 4.6}, {106, 4.3}, {107, 4.1}, {108, 3.9}, {109, 3.7}, {110, 3.5}, {111, 3.4},
            {112, 3.3}, {113, 3.1}, {114, 3.0}, {115, 2.9}, {116, 2.8}, {117, 2.7}, {118, 2.5}, {119, 2.3}, {120, 2.0}
        };

        // Cached market data
        static MarketData _cachedMarketData = null;
        static DateTime _marketDataFetchTime = DateTime.MinValue;

        // STATE TAX DATABASE (2026 Projected)
        static Dictionary<string, StateTaxProfile> _stateTaxDatabase = null;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Initialize state tax database
            InitializeStateTaxDatabase();

            bool runAgain = true;
            int analysisCount = 0;

            while (runAgain)
            {
                try
                {
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("=================================================================");
                    Console.WriteLine("   RETIREMENT NAVIGATOR (SAPPHIRE EDITION v8.0)");
                    Console.WriteLine("   Intelligent Multi-State Tax Analysis System");
                    Console.WriteLine("=================================================================");
                    Console.ResetColor();

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Note: This tool provides estimates only - not financial advice");
                    Console.ResetColor();

                    int currentYear = DateTime.Now.Year;
                    analysisCount++;

                    // === SECTION 1: PROFILE INPUTS ===
                    Console.WriteLine("\n--- PROFILE INFORMATION ---");

                    Console.Write("Enter Name/Label (e.g., 'Dad', 'Self', 'Mom'): ");
                    string personName = Console.ReadLine()?.Trim();
                    if (string.IsNullOrEmpty(personName)) personName = $"Person {analysisCount}";

                    int birthYear = (int)GetInput("Enter Birth Year (e.g., 1959): ");
                    int age = currentYear - birthYear;

                    // Input validation
                    if (age < 18 || age > 120)
                    {
                        PrintWarning($"[VALIDATION] Age {age} seems unusual (birth year {birthYear}).");
                        Console.Write("Continue anyway? (Y/N): ");
                        if (Console.ReadLine()?.ToUpper() != "Y")
                        {
                            Console.WriteLine("Analysis cancelled. Starting over...");
                            System.Threading.Thread.Sleep(1500);
                            continue;
                        }
                    }

                    double monthlyNeed = GetInput("Enter Monthly Spending Need (Pre-Tax): ");
                    double socialSecurity = GetInput("Enter Annual Social Security (0 if none): ");

                    // Tax-exempt interest
                    Console.Write("Tax-Exempt Interest (muni bonds, etc., 0 if none): $");
                    double taxExemptInterest = 0;
                    double.TryParse(Console.ReadLine()?.Replace("$", "").Replace(",", "").Trim(), out taxExemptInterest);
                    if (taxExemptInterest < 0) taxExemptInterest = 0;

                    // Filing status
                    Console.Write("Filing Status (J=Joint, S=Single, press Enter for Joint): ");
                    string filingInput = Console.ReadLine()?.ToUpper().Trim();
                    string filingStatus = (filingInput == "S") ? "Single" : "Joint";

                    // === STATE SELECTION (INTELLIGENT LOOKUP) ===
                    Console.WriteLine("\n--- STATE TAX INFORMATION ---");
                    StateTaxProfile stateProfile = SelectState(filingStatus);

                    Console.WriteLine($"\n>> {personName}'s Profile:");
                    Console.WriteLine($"   Age: {age} | Filing: {filingStatus} | State: {stateProfile.Name}");
                    Console.WriteLine($"   Monthly Need: {monthlyNeed:C0} | SS: {socialSecurity:C0}/year");

                    // === SECTION 2: FINANCIAL BALANCES ===
                    Console.WriteLine("\n--- CURRENT BALANCES ---");

                    double cashBalance = GetInput($"Enter Current Cash Balance ({CashTicker}): ");
                    double stockBalance = GetInput($"Enter Current Stock Balance ({StockTicker}): ");

                    double priorYearEndBalance = 0;
                    if (age >= 72)
                    {
                        priorYearEndBalance = GetInput("Enter TOTAL IRA Value on Dec 31 of LAST YEAR (for RMD): ");

                        // Sanity check
                        double currentTotal = cashBalance + stockBalance;
                        double difference = Math.Abs(currentTotal - priorYearEndBalance);
                        double percentDiff = (priorYearEndBalance > 0) ? (difference / priorYearEndBalance) : 0;

                        if (percentDiff > 0.30)
                        {
                            PrintWarning($"[VALIDATION] Current total ({currentTotal:C0}) differs {percentDiff:P0} from prior year ({priorYearEndBalance:C0}).");
                            Console.WriteLine("This could indicate: market movement, withdrawals, or entry error.");
                            Console.Write("Continue with these values? (Y/N): ");
                            if (Console.ReadLine()?.ToUpper() != "Y")
                            {
                                Console.WriteLine("Please re-enter balances...");
                                System.Threading.Thread.Sleep(1000);
                                continue;
                            }
                        }
                    }

                    // Large balance confirmation
                    if (stockBalance > 5000000 || cashBalance > 2000000)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[LARGE BALANCE] Stock: {stockBalance:C0} | Cash: {cashBalance:C0}");
                        Console.Write("Press Enter to confirm or Ctrl+C to exit: ");
                        Console.ResetColor();
                        Console.ReadLine();
                    }

                    // Emergency expenses
                    Console.Write("Any One-Time Emergency Expenses Today? (Enter $ amount or 0): $");
                    double emergencyExpense = 0;
                    double.TryParse(Console.ReadLine()?.Replace("$", "").Replace(",", "").Trim(), out emergencyExpense);

                    if (emergencyExpense > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ADJUSTMENT] Deducting {emergencyExpense:C0} from Cash.");
                        cashBalance -= emergencyExpense;
                        Console.ResetColor();
                    }

                    double totalPortfolio = cashBalance + stockBalance;

                    // === SECTION 3: MARKET DATA ===
                    Console.WriteLine("\n--- MARKET DATA ---");

                    MarketData market;
                    if (_cachedMarketData != null && (DateTime.Now - _marketDataFetchTime).TotalMinutes < 5)
                    {
                        market = _cachedMarketData;
                        Console.WriteLine($"[Using cached market data from {_marketDataFetchTime:HH:mm:ss}]");
                    }
                    else
                    {
                        Console.WriteLine("[Fetching live market data...]");
                        market = await GetMarketData(StockTicker);
                        _cachedMarketData = market;
                        _marketDataFetchTime = DateTime.Now;
                    }

                    double estimatedDividends = stockBalance * market.DividendYield;

                    Console.WriteLine($"   {StockTicker} 1-Year Return: {market.ReturnPct:P2}");
                    Console.WriteLine($"   Current Price: {market.CurrentPrice:C2}");
                    Console.WriteLine($"   Dividend Yield: {market.DividendYield:P2}");

                    // === SECTION 4: ANALYSIS REPORT ===
                    Console.WriteLine("\n=================================================================");
                    Console.WriteLine($"            ANALYSIS REPORT FOR {personName.ToUpper()} ({currentYear})");
                    Console.WriteLine("=================================================================");

                    // A. RMD Check
                    int rmdStartAge = (birthYear >= 1960) ? 75 : 73;
                    double rmdFactor = GetRmdFactor(age, rmdStartAge);
                    double rmdAmount = (rmdFactor > 0 && priorYearEndBalance > 0) ? priorYearEndBalance / rmdFactor : 0;
                    double annualNeedFromIRA = monthlyNeed * 12;
                    double iraWithdrawal = Math.Max(annualNeedFromIRA, rmdAmount);

                    if (rmdAmount > annualNeedFromIRA)
                    {
                        PrintWarning($"[IRS ALERT] RMD ({rmdAmount:C0}) exceeds spending need ({annualNeedFromIRA:C0}).");
                        Console.WriteLine($"            You MUST withdraw {rmdAmount:C0} to avoid penalties.");
                    }
                    else if (rmdAmount > 0)
                    {
                        Console.WriteLine($"[IRS OK] Your spending ({iraWithdrawal:C0}) satisfies RMD requirement.");
                    }
                    else
                    {
                        Console.WriteLine($"[RMD] Not yet required (starts at age {rmdStartAge}).");
                    }

                    // B. Tax Estimation
                    double fedTax = CalculateFederalTaxDynamic(iraWithdrawal, socialSecurity, taxExemptInterest, filingStatus, currentYear);
                    double stateTax = CalculateStateTax(iraWithdrawal, socialSecurity, stateProfile, filingStatus);
                    double totalTax = fedTax + stateTax;
                    double totalGrossIncome = iraWithdrawal + socialSecurity;

                    Console.WriteLine($"\n[TAX ESTIMATE - {stateProfile.Name}]");
                    Console.WriteLine($"   Gross Income:     {totalGrossIncome:C0}");
                    Console.WriteLine($"   Federal Tax:      {fedTax:C0}");
                    Console.WriteLine($"   {stateProfile.Name} State Tax:    {stateTax:C0}");
                    if (!stateProfile.HasIncomeTax)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   ({stateProfile.Name} has no state income tax!)");
                        Console.ResetColor();
                    }
                    if (stateProfile.SocialSecurityExemptPct == 100)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   (Social Security fully exempt in {stateProfile.Name})");
                        Console.ResetColor();
                    }
                    Console.WriteLine($"   TOTAL Tax:        {totalTax:C0}");
                    Console.WriteLine($"   NET Income:       {totalGrossIncome - totalTax:C0}");
                    Console.WriteLine($"   Effective Rate:   {totalTax / totalGrossIncome:P1}");

                    // C. Dynamic Glide Path
                    int targetYears = (age < 75) ? 3 : (age < 85 ? 4 : 5);
                    double targetCash = 0;
                    for (int k = 0; k < targetYears; k++)
                        targetCash += iraWithdrawal * Math.Pow(1 + AnnualInflation, k);

                    Console.WriteLine($"\n[CASH STRATEGY]");
                    Console.WriteLine($"   Target Buffer:    {targetCash:C0} ({targetYears} years)");
                    Console.WriteLine($"   Current Cash:     {cashBalance:C0}");

                    double cashDeficit = targetCash - cashBalance;
                    if (cashDeficit > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Shortfall:        {cashDeficit:C0}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   Surplus:          {Math.Abs(cashDeficit):C0}");
                        Console.ResetColor();
                    }

                    double monthsOfCash = (iraWithdrawal > 0) ? cashBalance / (iraWithdrawal / 12) : 999;
                    Console.WriteLine($"   Months Covered:   {monthsOfCash:F1} months");

                    if (monthsOfCash < MinCashMonthsWarning)
                    {
                        PrintWarning($"\n   ⚠️  CASH ALERT: Only {monthsOfCash:F1} months of expenses remaining!");
                        Console.WriteLine("   Consider reducing discretionary spending.");
                    }

                    // D. Monte Carlo Simulation
                    Console.WriteLine($"\n[SIMULATION] Running 10,000 scenarios to age 100...");
                    Console.WriteLine($"             (Stress-tested with {TaxSafetyMargin:P0} tax buffer)");

                    var survivalStats = RunMilestoneSimulation(
                        stockBalance,
                        cashBalance,
                        iraWithdrawal * (1 + TaxSafetyMargin),
                        age,
                        rmdStartAge,
                        market.DividendYield
                    );

                    if (survivalStats.Count > 0)
                    {
                        Console.WriteLine("\n   --- PORTFOLIO SURVIVAL FORECAST ---");
                        foreach (var stat in survivalStats.OrderBy(x => x.Key))
                        {
                            string status = stat.Value > 85 ? "✓ Safe" : (stat.Value > 50 ? "⚠ Caution" : "✗ RISK");
                            ConsoleColor color = stat.Value > 85 ? ConsoleColor.Green : (stat.Value > 50 ? ConsoleColor.Yellow : ConsoleColor.Red);

                            Console.ForegroundColor = color;
                            Console.Write($"   Age {stat.Key,3}: {stat.Value,5:F1}% {status}");
                            Console.ResetColor();

                            if (stat.Value < 50) Console.Write(" ◄◄◄");
                            Console.WriteLine();
                        }

                        // Overall assessment
                        var age80Survival = survivalStats.ContainsKey(80) ? survivalStats[80] : 100;
                        var age90Survival = survivalStats.ContainsKey(90) ? survivalStats[90] : 100;

                        Console.WriteLine($"\n   [SUMMARY]");
                        if (age80Survival > 90 && age90Survival > 75)
                        {
                            PrintSuccess("   ✓ Strong financial position across most scenarios.");
                        }
                        else if (age80Survival > 70 && age90Survival > 50)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("   ⚠ Adequate but monitor closely. Consider reducing spending if possible.");
                            Console.ResetColor();
                        }
                        else
                        {
                            PrintWarning("   ✗ High risk of portfolio depletion. Recommend professional consultation.");
                        }
                    }

                    // === SECTION 5: DECISION ENGINE ===
                    Console.WriteLine("\n=================================================================");
                    Console.WriteLine("                    RECOMMENDED ACTIONS");
                    Console.WriteLine("=================================================================");

                    string logAction = "";
                    double rawDeficit = targetCash - cashBalance;

                    if (cashBalance >= targetCash)
                    {
                        PrintSuccess("✅ ACTION: HOLD & WITHDRAW");
                        Console.WriteLine($"   1. Do NOT sell any {StockTicker} today.");
                        Console.WriteLine($"   2. Your cash buffer ({cashBalance:C0}) exceeds target ({targetCash:C0}).");
                        Console.WriteLine($"   3. Withdraw monthly income ({iraWithdrawal / 12:C0}) strictly from {CashTicker}.");
                        Console.WriteLine($"   4. Let dividends accumulate in {CashTicker} naturally.");
                        logAction = "HOLD_FULL";
                    }
                    else if (market.ReturnPct > 0)
                    {
                        PrintSuccess("🟢 ACTION: REBALANCE (MARKET UP - HARVEST GAINS)");
                        Console.WriteLine($"   Market is UP {market.ReturnPct:P2} over the past year.");
                        Console.WriteLine($"   Cash buffer is SHORT by {rawDeficit:C0}.");
                        Console.WriteLine($"\n   >>> EXECUTE IN FIDELITY:");
                        Console.WriteLine($"   1. SELL  ${rawDeficit:F2} of {StockTicker}");
                        Console.WriteLine($"   2. BUY   ${rawDeficit:F2} of {CashTicker}");
                        Console.WriteLine($"      (Or let it settle in Core Position automatically)");
                        Console.WriteLine($"\n   REASON: Locking in gains to refill {targetYears}-year safety buffer.");
                        logAction = "REFILL_GAINS";
                    }
                    else
                    {
                        PrintWarning("🔴 ACTION: HOLD (MARKET DOWN - PRESERVE CAPITAL)");
                        Console.WriteLine($"   Market is DOWN {market.ReturnPct:P2} over the past year.");
                        Console.WriteLine($"   Cash is below target, but DO NOT sell stocks now.");
                        Console.WriteLine($"\n   >>> INSTRUCTIONS:");
                        Console.WriteLine($"   1. Continue spending from {CashTicker} ({monthsOfCash:F1} months remaining)");
                        Console.WriteLine($"   2. Reduce discretionary expenses where possible");
                        Console.WriteLine($"   3. Monitor dividend payments - they continue even in down markets");

                        if (estimatedDividends > MinDividendAlert)
                        {
                            Console.WriteLine($"\n   💡 TIP: Check {CashTicker} for approximately {estimatedDividends:C0}");
                            Console.WriteLine($"           in annual dividends (paid quarterly).");
                        }

                        if (monthsOfCash < 3)
                        {
                            PrintWarning($"\n   ⚠️  CRITICAL: Only {monthsOfCash:F1} months of cash remaining!");
                            Console.WriteLine("   If market doesn't recover within 3 months, may need emergency strategy.");
                            Console.WriteLine("   Strongly recommend consulting a fiduciary financial advisor.");
                        }

                        logAction = "HOLD_BEAR";
                    }

                    // === SECTION 6: LOGGING ===
                    LogRun(personName, stateProfile.Name, logAction, totalPortfolio, iraWithdrawal, market.ReturnPct, age, cashBalance, totalTax);

                    string fullLogPath = Path.GetFullPath(LogFileName);
                    Console.WriteLine($"\n=================================================================");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"Analysis saved to: {fullLogPath}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    PrintWarning($"\n!!! ERROR OCCURRED !!!");
                    Console.WriteLine($"Type: {ex.GetType().Name}");
                    Console.WriteLine($"Message: {ex.Message}");
                    Console.WriteLine($"\nIf this persists, please verify all inputs are valid numbers.");
                }

                // === RUN AGAIN PROMPT ===
                Console.WriteLine("\n-----------------------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Run analysis for another person? (Y/N): ");
                Console.ResetColor();
                string response = Console.ReadLine()?.Trim().ToUpper();
                runAgain = (response == "Y" || response == "YES");

                if (!runAgain)
                {
                    Console.WriteLine("\n=================================================================");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Session complete. {analysisCount} analysis(es) performed.");
                    Console.ResetColor();
                    Console.WriteLine("=================================================================");
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        // --- STATE TAX SYSTEM ---

        static void InitializeStateTaxDatabase()
        {
            _stateTaxDatabase = new Dictionary<string, StateTaxProfile>(StringComparer.OrdinalIgnoreCase);

            // NO INCOME TAX STATES (2026)
            var noTaxStates = new[] { "Alaska", "Florida", "Nevada", "New Hampshire", "South Dakota", "Tennessee", "Texas", "Washington", "Wyoming" };
            foreach (var state in noTaxStates)
            {
                _stateTaxDatabase[state] = new StateTaxProfile
                {
                    Name = state,
                    HasIncomeTax = false,
                    SocialSecurityExemptPct = 100,
                    Notes = "No state income tax"
                };
            }

            // MASSACHUSETTS (Flat 5.0%, High Deductions, SS Exempt)
            _stateTaxDatabase["Massachusetts"] = new StateTaxProfile
            {
                Name = "Massachusetts",
                HasIncomeTax = true,
                FlatRate = 0.05,
                SingleStdDeduction = 0, // MA doesn't have standard deduction - uses exemptions
                JointStdDeduction = 0,
                RetirementExclusion = 0, // No special retirement exclusion
                SocialSecurityExemptPct = 100, // SS is fully exempt
                Notes = "Flat 5.0% tax, Social Security exempt"
            };

            // CALIFORNIA (Progressive, High Taxes)
            _stateTaxDatabase["California"] = new StateTaxProfile
            {
                Name = "California",
                HasIncomeTax = true,
                IsProgressive = true,
                SingleStdDeduction = 5363,
                JointStdDeduction = 10726,
                SocialSecurityExemptPct = 100,
                Brackets = new List<TaxBracket>
                {
                    new TaxBracket { Threshold = 10412, Rate = 0.01 },
                    new TaxBracket { Threshold = 24684, Rate = 0.02 },
                    new TaxBracket { Threshold = 38959, Rate = 0.04 },
                    new TaxBracket { Threshold = 54081, Rate = 0.06 },
                    new TaxBracket { Threshold = 68350, Rate = 0.08 },
                    new TaxBracket { Threshold = 349137, Rate = 0.093 },
                    new TaxBracket { Threshold = 418961, Rate = 0.103 },
                    new TaxBracket { Threshold = 698271, Rate = 0.113 },
                    new TaxBracket { Threshold = double.MaxValue, Rate = 0.123 }
                },
                Notes = "Progressive rates, SS exempt"
            };

            // NEW YORK (Progressive, High Taxes)
            _stateTaxDatabase["New York"] = new StateTaxProfile
            {
                Name = "New York",
                HasIncomeTax = true,
                IsProgressive = true,
                SingleStdDeduction = 8000,
                JointStdDeduction = 16050,
                SocialSecurityExemptPct = 100,
                Brackets = new List<TaxBracket>
                {
                    new TaxBracket { Threshold = 8500, Rate = 0.04 },
                    new TaxBracket { Threshold = 11700, Rate = 0.045 },
                    new TaxBracket { Threshold = 13900, Rate = 0.0525 },
                    new TaxBracket { Threshold = 80650, Rate = 0.055 },
                    new TaxBracket { Threshold = 215400, Rate = 0.06 },
                    new TaxBracket { Threshold = 1077550, Rate = 0.0685 },
                    new TaxBracket { Threshold = 5000000, Rate = 0.0965 },
                    new TaxBracket { Threshold = 25000000, Rate = 0.103 },
                    new TaxBracket { Threshold = double.MaxValue, Rate = 0.109 }
                },
                Notes = "Progressive rates, SS exempt"
            };

            // PENNSYLVANIA (Flat 3.07%, No SS Tax)
            _stateTaxDatabase["Pennsylvania"] = new StateTaxProfile
            {
                Name = "Pennsylvania",
                HasIncomeTax = true,
                FlatRate = 0.0307,
                SingleStdDeduction = 0,
                JointStdDeduction = 0,
                SocialSecurityExemptPct = 100,
                RetirementExclusion = double.MaxValue, // All retirement income exempt
                Notes = "Flat 3.07%, retirement income and SS exempt"
            };

            // NORTH CAROLINA (Flat 4.5%, SS Exempt)
            _stateTaxDatabase["North Carolina"] = new StateTaxProfile
            {
                Name = "North Carolina",
                HasIncomeTax = true,
                FlatRate = 0.045,
                SingleStdDeduction = 12750,
                JointStdDeduction = 25500,
                SocialSecurityExemptPct = 100,
                Notes = "Flat 4.5%, SS exempt"
            };

            // ARIZONA (Flat 2.5%, SS Partially Exempt)
            _stateTaxDatabase["Arizona"] = new StateTaxProfile
            {
                Name = "Arizona",
                HasIncomeTax = true,
                FlatRate = 0.025,
                SingleStdDeduction = 13850,
                JointStdDeduction = 27700,
                SocialSecurityExemptPct = 100, // Fully exempt as of 2023
                Notes = "Flat 2.5%, SS exempt"
            };

            // OREGON (Progressive, High, SS Exempt)
            _stateTaxDatabase["Oregon"] = new StateTaxProfile
            {
                Name = "Oregon",
                HasIncomeTax = true,
                IsProgressive = true,
                SingleStdDeduction = 2605,
                JointStdDeduction = 5210,
                SocialSecurityExemptPct = 100,
                Brackets = new List<TaxBracket>
                {
                    new TaxBracket { Threshold = 4050, Rate = 0.0475 },
                    new TaxBracket { Threshold = 10200, Rate = 0.0675 },
                    new TaxBracket { Threshold = 125000, Rate = 0.0875 },
                    new TaxBracket { Threshold = double.MaxValue, Rate = 0.099 }
                },
                Notes = "Progressive rates, SS exempt"
            };

            // COLORADO (Flat 4.4%, SS Partially Exempt for Seniors)
            _stateTaxDatabase["Colorado"] = new StateTaxProfile
            {
                Name = "Colorado",
                HasIncomeTax = true,
                FlatRate = 0.044,
                SingleStdDeduction = 0, // Uses federal
                JointStdDeduction = 0,
                RetirementExclusion = 24000, // Up to $24k exempt for 65+
                SocialSecurityExemptPct = 100,
                Notes = "Flat 4.4%, SS exempt, pension exclusion for 65+"
            };

            // Add more states as needed...
            // For brevity, adding a few more key retirement states

            // CONNECTICUT (Progressive, SS Exempt for Lower Incomes)
            _stateTaxDatabase["Connecticut"] = new StateTaxProfile
            {
                Name = "Connecticut",
                HasIncomeTax = true,
                IsProgressive = true,
                SingleStdDeduction = 0,
                JointStdDeduction = 0,
                SocialSecurityExemptPct = 100, // For AGI < $75k (single) / $100k (joint)
                Brackets = new List<TaxBracket>
                {
                    new TaxBracket { Threshold = 10000, Rate = 0.03 },
                    new TaxBracket { Threshold = 50000, Rate = 0.05 },
                    new TaxBracket { Threshold = 100000, Rate = 0.055 },
                    new TaxBracket { Threshold = 200000, Rate = 0.06 },
                    new TaxBracket { Threshold = 250000, Rate = 0.065 },
                    new TaxBracket { Threshold = 500000, Rate = 0.069 },
                    new TaxBracket { Threshold = double.MaxValue, Rate = 0.0699 }
                },
                Notes = "Progressive, SS exempt for lower incomes"
            };
        }

        static StateTaxProfile SelectState(string filingStatus)
        {
            while (true)
            {
                Console.Write("Enter State (full name or abbreviation): ");
                string input = Console.ReadLine()?.Trim();

                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("[Please enter a state name]");
                    continue;
                }

                // Try direct lookup
                if (_stateTaxDatabase.TryGetValue(input, out var profile))
                {
                    DisplayStateInfo(profile, filingStatus);
                    return profile;
                }

                // Try common abbreviations
                var stateAbbreviations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    {"MA", "Massachusetts"}, {"CA", "California"}, {"NY", "New York"},
                    {"FL", "Florida"}, {"TX", "Texas"}, {"PA", "Pennsylvania"},
                    {"NC", "North Carolina"}, {"AZ", "Arizona"}, {"OR", "Oregon"},
                    {"CO", "Colorado"}, {"CT", "Connecticut"}, {"WA", "Washington"},
                    {"NV", "Nevada"}, {"TN", "Tennessee"}, {"NH", "New Hampshire"},
                    {"AK", "Alaska"}, {"SD", "South Dakota"}, {"WY", "Wyoming"}
                };

                if (stateAbbreviations.TryGetValue(input, out string fullName) &&
                    _stateTaxDatabase.TryGetValue(fullName, out profile))
                {
                    DisplayStateInfo(profile, filingStatus);
                    return profile;
                }

                // State not in database
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{input} not in database. Using manual entry.]");
                Console.ResetColor();

                return CreateCustomStateProfile(input, filingStatus);
            }
        }

        static void DisplayStateInfo(StateTaxProfile profile, string filingStatus)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✓ {profile.Name} Tax Profile Loaded (2026 Projected)");
            Console.ResetColor();

            if (!profile.HasIncomeTax)
            {
                Console.WriteLine($"   • No state income tax");
            }
            else if (profile.FlatRate > 0)
            {
                Console.WriteLine($"   • Flat Rate: {profile.FlatRate:P2}");
            }
            else if (profile.IsProgressive)
            {
                Console.WriteLine($"   • Progressive brackets: {profile.Brackets[0].Rate:P1} - {profile.Brackets[profile.Brackets.Count - 1].Rate:P2}");
            }

            double deduction = (filingStatus == "Joint") ? profile.JointStdDeduction : profile.SingleStdDeduction;
            if (deduction > 0)
            {
                Console.WriteLine($"   • Standard Deduction ({filingStatus}): {deduction:C0}");
            }

            if (profile.SocialSecurityExemptPct == 100)
            {
                Console.WriteLine($"   • Social Security: Fully Exempt");
            }
            else if (profile.SocialSecurityExemptPct > 0)
            {
                Console.WriteLine($"   • Social Security: {profile.SocialSecurityExemptPct}% Exempt");
            }

            if (profile.RetirementExclusion > 0 && profile.RetirementExclusion < double.MaxValue)
            {
                Console.WriteLine($"   • Retirement Exclusion: {profile.RetirementExclusion:C0}");
            }
            else if (profile.RetirementExclusion == double.MaxValue)
            {
                Console.WriteLine($"   • Retirement Income: Fully Exempt");
            }

            if (!string.IsNullOrEmpty(profile.Notes))
            {
                Console.WriteLine($"   • Note: {profile.Notes}");
            }
        }

        static StateTaxProfile CreateCustomStateProfile(string stateName, string filingStatus)
        {
            Console.WriteLine($"\nManual entry for {stateName}:");

            Console.Write("Does this state have income tax? (Y/N): ");
            bool hasTax = Console.ReadLine()?.ToUpper() == "Y";

            if (!hasTax)
            {
                return new StateTaxProfile
                {
                    Name = stateName,
                    HasIncomeTax = false,
                    SocialSecurityExemptPct = 100
                };
            }

            double rate = GetInput("Enter flat tax rate (e.g., 0.05 for 5%): ");
            double deduction = GetInput($"Enter standard deduction for {filingStatus}: ");

            Console.Write("Is Social Security taxed? (Y/N): ");
            bool ssTaxed = Console.ReadLine()?.ToUpper() == "Y";

            return new StateTaxProfile
            {
                Name = stateName,
                HasIncomeTax = true,
                FlatRate = rate,
                SingleStdDeduction = (filingStatus == "Single") ? deduction : 0,
                JointStdDeduction = (filingStatus == "Joint") ? deduction : 0,
                SocialSecurityExemptPct = ssTaxed ? 0 : 100,
                Notes = "Custom entry"
            };
        }

        static double CalculateStateTax(double iraWithdrawal, double socialSecurity, StateTaxProfile state, string filingStatus)
        {
            if (!state.HasIncomeTax) return 0;

            // Calculate taxable SS for state
            double taxableSS = socialSecurity * (1 - state.SocialSecurityExemptPct / 100.0);

            // Calculate taxable IRA (after retirement exclusion if applicable)
            double taxableIRA = iraWithdrawal;
            if (state.RetirementExclusion > 0)
            {
                taxableIRA = Math.Max(0, iraWithdrawal - state.RetirementExclusion);
            }

            double totalIncome = taxableIRA + taxableSS;

            // Apply standard deduction
            double stdDeduction = (filingStatus == "Joint") ? state.JointStdDeduction : state.SingleStdDeduction;
            double taxableIncome = Math.Max(0, totalIncome - stdDeduction);

            if (taxableIncome <= 0) return 0;

            // Flat tax
            if (state.FlatRate > 0)
            {
                return taxableIncome * state.FlatRate;
            }

            // Progressive brackets
            if (state.IsProgressive && state.Brackets != null)
            {
                double tax = 0;
                double previousThreshold = 0;

                foreach (var bracket in state.Brackets)
                {
                    if (taxableIncome <= previousThreshold) break;

                    double taxableInThisBracket = Math.Min(taxableIncome, bracket.Threshold) - previousThreshold;
                    tax += taxableInThisBracket * bracket.Rate;
                    previousThreshold = bracket.Threshold;

                    if (taxableIncome <= bracket.Threshold) break;
                }

                return tax;
            }

            return 0;
        }

        // --- CORE LOGIC (Federal Tax, RMD, etc.) ---

        static double GetRmdFactor(int age, int startAge)
        {
            if (age < startAge) return 0;
            if (RmdTable.TryGetValue(age, out double factor)) return factor;
            return (age >= 120) ? 2.0 : 0;
        }

        static double CalculateFederalTaxDynamic(double iraWithdrawal, double socialSecurity, double taxExemptInterest, string filingStatus, int currentYear)
        {
            const int BaseYear = 2024;
            int yearDelta = Math.Max(0, currentYear - BaseYear);
            double indexFactor = Math.Pow(1 + AnnualInflation, yearDelta);

            // Provisional Income Test
            double provisionalIncome = iraWithdrawal + (socialSecurity * 0.5) + taxExemptInterest;
            double taxableSS = 0;

            if (filingStatus == "Joint")
            {
                if (provisionalIncome > 44000)
                {
                    double tier1 = 12000 * 0.50;
                    double tier2 = (provisionalIncome - 44000) * 0.85;
                    taxableSS = tier1 + tier2;
                }
                else if (provisionalIncome > 32000)
                {
                    taxableSS = (provisionalIncome - 32000) * 0.50;
                }
            }
            else
            {
                if (provisionalIncome > 34000)
                {
                    double tier1 = 9000 * 0.50;
                    double tier2 = (provisionalIncome - 34000) * 0.85;
                    taxableSS = tier1 + tier2;
                }
                else if (provisionalIncome > 25000)
                {
                    taxableSS = (provisionalIncome - 25000) * 0.50;
                }
            }

            taxableSS = Math.Min(taxableSS, socialSecurity * 0.85);

            // Standard Deduction
            double baseStdDed = (filingStatus == "Joint") ? 29200 : 14600;
            double over65Add = (filingStatus == "Joint") ? 1550 : 1950;
            double additionalDeduction = (filingStatus == "Joint") ? over65Add * 2 : over65Add;
            double totalStdDed = (baseStdDed + additionalDeduction) * indexFactor;

            double adjustedGrossIncome = iraWithdrawal + taxableSS;
            double taxableIncome = Math.Max(0, adjustedGrossIncome - totalStdDed);

            if (taxableIncome <= 0) return 0;

            // Brackets
            double[] brackets = (filingStatus == "Joint")
                ? new double[] { 23200, 94300, 201050, 383900, 487450, 731200 }
                : new double[] { 11600, 47150, 100525, 191950, 243725, 609350 };

            for (int i = 0; i < brackets.Length; i++)
                brackets[i] *= indexFactor;

            double tax = 0;

            double b1 = Math.Min(taxableIncome, brackets[0]);
            tax += b1 * 0.10;
            if (taxableIncome <= brackets[0]) return tax;

            double b2 = Math.Min(taxableIncome, brackets[1]) - brackets[0];
            tax += b2 * 0.12;
            if (taxableIncome <= brackets[1]) return tax;

            double b3 = Math.Min(taxableIncome, brackets[2]) - brackets[1];
            tax += b3 * 0.22;
            if (taxableIncome <= brackets[2]) return tax;

            double b4 = Math.Min(taxableIncome, brackets[3]) - brackets[2];
            tax += b4 * 0.24;
            if (taxableIncome <= brackets[3]) return tax;

            double b5 = Math.Min(taxableIncome, brackets[4]) - brackets[3];
            tax += b5 * 0.32;
            if (taxableIncome <= brackets[4]) return tax;

            double b6 = Math.Min(taxableIncome, brackets[5]) - brackets[4];
            tax += b6 * 0.35;
            if (taxableIncome <= brackets[5]) return tax;

            double b7 = taxableIncome - brackets[5];
            tax += b7 * 0.37;

            return tax;
        }

        static Dictionary<int, double> RunMilestoneSimulation(
            double startStock,
            double startCash,
            double startWithdrawal,
            int startAge,
            int rmdStartAge,
            double currentDivYield)
        {
            int sims = 10000;
            var survivalCounts = new Dictionary<int, int>();
            var milestones = new List<int>();

            int nextMilestone = ((startAge / 5) + 1) * 5;
            while (nextMilestone <= 100)
            {
                milestones.Add(nextMilestone);
                nextMilestone += 5;
            }

            foreach (int m in milestones)
                survivalCounts[m] = 0;

            Random r = new Random();
            double divYield = (currentDivYield > 0 && currentDivYield < 0.10) ? currentDivYield : 0.015;

            for (int sim = 0; sim < sims; sim++)
            {
                double stocks = startStock;
                double cash = startCash;
                double withdrawal = startWithdrawal;
                bool bankrupt = false;
                int currentAge = startAge;
                int histIndex = r.Next(HistoricalReturns.Length);

                while (currentAge <= 100)
                {
                    double rmdFactor = GetRmdFactor(currentAge, rmdStartAge);
                    double rmdAmt = (rmdFactor > 0) ? (stocks + cash) / rmdFactor : 0;
                    double actualWithdrawal = Math.Max(withdrawal, rmdAmt);

                    int targetYears = (currentAge < 75) ? 3 : (currentAge < 85 ? 4 : 5);
                    double targetCashAmt = 0;
                    for (int k = 0; k < targetYears; k++)
                        targetCashAmt += actualWithdrawal * Math.Pow(1 + AnnualInflation, k);

                    if (cash >= actualWithdrawal)
                    {
                        cash -= actualWithdrawal;
                    }
                    else
                    {
                        double remainder = actualWithdrawal - cash;
                        cash = 0;
                        stocks -= remainder;
                    }

                    if (stocks + cash <= 0)
                        bankrupt = true;

                    if (milestones.Contains(currentAge) && !bankrupt)
                        survivalCounts[currentAge]++;

                    if (bankrupt)
                    {
                        currentAge++;
                        continue;
                    }

                    double totalReturn = HistoricalReturns[histIndex];
                    histIndex = (histIndex + 1) % HistoricalReturns.Length;

                    double priceReturn = totalReturn - divYield;
                    stocks *= (1 + priceReturn);
                    double dividends = stocks * divYield;
                    cash += dividends;

                    if (totalReturn > 0 && cash < targetCashAmt)
                    {
                        double deficit = targetCashAmt - cash;
                        double toSell = Math.Min(stocks, deficit);
                        stocks -= toSell;
                        cash += toSell;
                    }

                    withdrawal *= (1 + AnnualInflation);
                    currentAge++;
                }
            }

            var results = new Dictionary<int, double>();
            foreach (var kvp in survivalCounts)
            {
                results[kvp.Key] = (double)kvp.Value / sims * 100.0;
            }

            return results;
        }

        static async Task<MarketData> GetMarketData(string ticker)
        {
            var data = new MarketData { ReturnPct = -999 };

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(8);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?interval=1mo&range=1y&events=div";
                    string json = await client.GetStringAsync(url);

                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
                        var meta = result.GetProperty("meta");

                        double previousClose = meta.GetProperty("chartPreviousClose").GetDouble();
                        double currentPrice = meta.GetProperty("regularMarketPrice").GetDouble();

                        data.CurrentPrice = currentPrice;
                        data.ReturnPct = (currentPrice - previousClose) / previousClose;

                        double totalDividends = 0;
                        if (result.TryGetProperty("events", out var events) &&
                            events.TryGetProperty("dividends", out var dividends))
                        {
                            foreach (JsonProperty dividend in dividends.EnumerateObject())
                            {
                                if (dividend.Value.TryGetProperty("amount", out var amount))
                                {
                                    totalDividends += amount.GetDouble();
                                }
                            }
                        }

                        if (currentPrice > 0)
                        {
                            data.DividendYield = totalDividends / previousClose;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"   [API Error] {ex.Message}");
                Console.ResetColor();
            }

            if (data.ReturnPct == -999)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n   [MANUAL ENTRY REQUIRED]");
                Console.ResetColor();

                data.ReturnPct = GetInput("   1-Year Return (e.g., 0.10 for 10%): ");
                data.DividendYield = GetInput("   Dividend Yield (e.g., 0.015 for 1.5%): ");
                data.CurrentPrice = 0;
            }

            return data;
        }

        static void LogRun(string personName, string state, string action, double portfolioVal, double withdrawAmt, double marketReturn, int age, double cashBal, double totalTax)
        {
            string logPath = LogFileName;
            bool fileExists = File.Exists(logPath);

            if (!fileExists)
            {
                string header = "Date,Time,Person,State,Age,TotalPortfolio,CashBalance,AnnualWithdrawal,TotalTax,MarketReturn1Y,Action" + Environment.NewLine;
                File.WriteAllText(logPath, header);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd,HH:mm:ss");
            string line = $"{timestamp},{personName},{state},{age},{portfolioVal:F0},{cashBal:F0},{withdrawAmt:F0},{totalTax:F0},{marketReturn:F4},{action}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }

        static double GetInput(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Replace("$", "").Replace(",", "").Replace("%", "").Trim();

                if (string.IsNullOrEmpty(input))
                {
                    Console.WriteLine("   [Please enter a value]");
                    continue;
                }

                if (double.TryParse(input, out double value) && value >= 0)
                {
                    return value;
                }

                Console.WriteLine("   [Invalid - enter a positive number]");
            }
        }

        static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        static void PrintWarning(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}