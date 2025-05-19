Replication of "A Correlated Signals Algorithmic Game Theory Model of Litigation Bargaining"

To replicate the article, open the ACESim solution in Visual Studio or Visual Studio Code. The code uses the open source C# programming language and .Net Core libraries,
and it can be executed on a Windows PC. 

To replicate all simulations, download this repository to your hard drive. Within the ACESim4 folder will be a number of folders, such as ACESimBase, ACESimConsole, and so forth. Set the startup project to ACESimDistributed (if using just one core) or ACESimDistributedSaturate (if using many cores). Note that depending on what is run, using many cores may require a high amount of RAM, e.g. 128 GB, and even this may be insufficient. If there is a power or other failure, you can rerun the code. It will not repeat simulations that have already completed. When running is complete, you should check whether the ReportResults folder includes any files indicating that there were unrecoverable failures for certain simulations by looking for the word "Failure". The file will indicate the source of the failure, most like an out-of-memory condition. If that is so, delete or rename the file that includes the word "Coordinator"in the ReportResults folder, delete the failure files as well, and then set EvolutionSettings.SkipAltogetherIfEquilibriumFileAlreadyExists to true. It will then rerun only those simulations that failed. 

To execute the simulations over a number of computers in the Azure cloud instead of on your local machine, set EvolutionSettings.SaveToAzureBlob to true and then modify the class named cloudpw to use your own Azure credentials. The function should be called GetCloudStorageAccountConnectionString() and should return a string containing valid credentials for an Azure blob account. 
Then, rent virtual machines on Azure and run ACESimDistributed or ACESimDistributedSaturate on each of them. Different computers will then work on different simulations. They will coordinate by sharing information over an Azure blob.

When the code has completed executing, the results of the main simulations will be saved in the ReportResults folder. The results will be in the form of a number of .csv files and other files, such as .efg files (showing the structure of each extensive form game), .equ files (showing the equilibrium of each game), and .tex files (showing the LaTeX code for the diagrams that illustrate individual simulations). To generate diagrams that aggregate information across simulations, make sure that Latex and Texmaker are installed. Set the startup project to LitigCharts and run the code. It will then organize all of the report outputs and launch Texmaker to generate more Latex diagrams. 

To rerun the simulations but for a smaller tree size but with more equilibria (as in the robustness checking), please make the following changes: (1) Remove all results from the ReportResults folder. (2) Change the UseSmallerTree variable in LitigGameLauncher to true. (3) Change the SequenceFormNumPriorsToUseToGenerateEquilibria variable in EvolutionSettings to 50. Note that this will result in many more files being produced, one for each equilibrium. (4) Set DataProcessingBase.firstEqOnly to false if you want diagrams for average and correlated equilibrium. (5) Set MasterReportNameForDistributedProcessing in LitigGameCorrelatedSignalsArticleLauncher to avoid confusion with the larger tree size.

To run only the two baseline models (on the larger tree), change the OptionSetChosen variable in LitigGameLauncher to OptionSetChoice.FeeShiftingArticleBaselineOnly. If you wish to produce the coefficient-of-variation calculations in the article, set doCoefficientOfVariationCalculations in Runner.cs to true. 

To produce the diagrams of signal quality, set doSignalsDiagram in Runner.cs to true. Settings (such as noise level and whether to create a chart for liability or for damages levels) can be changed in the SignalsDiagram.cs class.

To print the game tree, set EvolutionSettings.PrintGameTree = true and EvolutionSettings.PrintedGameTreeIncludesInformationSetData = true. Then, you will want to run only a single model. Thus, set the startup project to ACESimConsole. In LitigGameOptionsGenerator.cs, you can change LitigGameChoice to LitigGameOptionSetChoices.SuperSimple, which is the very simple game with just 2 levels of litigation quality, 2 levels of signals, and 2 levels of offers. If you want to print only part of the diagram, change Exclusions in LitigGameDefinition.cs to TreeDiagramExclusions.BeginningOfGame or TreeDiagramExclusions.EndOfGame. 

To run the information set pressure analysis, set ACESimConsole as the startup project. Then set LitigGameChoice in LitigGameOptionsGenerator to LitigGameOptionSetChoices.BritishRuleZeroMultiplier. Set EvolutionSettings.IdentifyPressureOnInformationSets = true.

Overview of code

This repository contains code for applying a number of different approaches to solving extensive-form games and in particular different versions of a litigation game.

The ACESimBase folder contains the core code for finding equilibria. The GameSolvingAlgorithms folder within it contains code for a variety of different algorithms. The code that 
coordinates the use of the von Stengel et al. algorithm is located in SequenceForm.cs, which in turn executes code devoted to the algorithm (adapted from code provided von Stengel et al.)
in the ECTAAlgorithm folder. 

The code specifically relevant to the correlated signals game is contained within the ACESimBase/Games/LitigGame folder. Some of the options used by this game are not used in this paper. 
The ManualReports folder contains code to generate the Latex code for two of the diagram types; other code to generate diagrams is in the LitigCharts project. 
The specification of how to produce cross-tabulations of all the results is defined in the LitigGameReportDefinition.cs file.
The structure of the game tree, including the ordering of the decisions, is affected by LitigGameDefinition.cs. 
The game play itself is defined in LitigGame.cs, and the data structure
used to keep track of progress in the game is in LitigGameProgress.cs. 
The players, including chance players corresponding to different chance decisions, are listed in LitigGamePlayers.cs. 
The LitigGameCorrelatedSignalsArticleLauncher.cs file generates options for the many different permutations executed for this article.

