Replication of "Modeling Fee Shifting With Computational Game Theory"

To replicate the article, open the ACESim solution in Visual Studio or Visual Studio Code. The code uses the open source C# programming language and .Net Core libraries,
and it can be executed on Windows, Linux, and OSX.

To replicate just a single simulation, set the startup project to ACESimConsole, set to Release mode, and press Control-F5. If you want to change the settings used, 
find the FeeShiftingArticleBase() function in the LitigGameOptionsGenerator.cs file. The results will appear in the ReportResults directory. 

To replicate all simulations, set the startup project to ACESimDistributed (if using just one core) or ACESimDistributedSaturate (if using many cores). 
Allow approximately 450 core hours for the processing to complete. If there is a power or other failure, the code will not repeat simulations that have already 
completed. To execute the simulations over a number of computers in the Azure cloud, set EvolutionSettings.SaveToAzureBlob to true and then create a static 
class in the ACESimBase > Resources folder named cloudpw. The function should be called GetCloudStorageAccountConnectionString() and should return a string
containing valid credentials for an Azure blob account. Then, rent virtual machines on Azure and run ACESimDistributed or ACESimDistributedSaturate on each of
them. Different computers will then work on different simulations.

To replicate the simulations with a different game tree size, combine the two steps above: Modify FeeShiftingArticleBase(), then run ACESimDistributed or ACESimDistributedSaturate.

After the code completes, charts can be produced by setting LitigCharts as the startup project. The code in FeeShiftingDataProcessing.cs assumes that you have Latex preinstalled,
and in particular that C:\Users\Admin\AppData\Local\Programs\MiKTeX\miktex\bin\x64\pdflatex.exe contains the pdflatex program. You can change that to another directory (including
using a Linux-style address). 


Overview of code

This repository contains code for applying a number of different approaches to solving extensive-form games and in particular different versions of a litigation game.

The ACESimBase folder contains the core code for finding equilibria. The GameSolvingAlgorithms folder within it contains code for a variety of different algorithms. The code that 
coordinates the use of the von Stengel et al. algorithm is located in SequenceForm.cs, which in turn executes code devoted to the algorithm (adapted from code provided von Stengel et al.)
in the ECTAAlgorithm folder. 

The code specifically relevant to the litigation game is contained within the LitigGame folder. Some of the options used by this game are not used in this paper. The DisputeGeneration
folder contains alternative modules that can be used to generate disputes. The ManualReports folder contains code to generate the Latex code for two of the diagram types; other code
to generate diagrams is in the LitigCharts project. The content of spreadsheets that provide cross-tabulations of all the results is defined in the LitigGameReportDefinition folder.
The structure of the game tree, including the ordering of the decisions, is affected by LitigGameDefinition.cs. The game play itself is defined in LitigGame.cs, and the data structure
used to keep track of progress in the litigation game is LitigGameProgress. The players, including chance players corresponding to different chance decisions, are listed in LitigGamePlayers. 
The LitigGameLauncher.cs file generates options for the many different permutations executed. 

