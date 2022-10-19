Replication of "Modeling Fee Shifting With Computational Game Theory"

To replicate the article, open the ACESim solution in Visual Studio or Visual Studio Code. The code uses the open source C# programming language and .Net Core libraries,
and it can be executed on a Windows PC. 

To replicate all simulations, set the startup project to ACESimDistributed (if using just one core) or ACESimDistributedSaturate (if using many cores). 
Create an empty folder called "C:\Primary results" on the hard drive.
If there is a power or other failure, the code will not repeat simulations that have already completed. To execute the simulations over a number of 
computers in the Azure cloud, set EvolutionSettings.SaveToAzureBlob to true and then create a static class in the ACESimBase > Resources folder named cloudpw. 
The function should be called GetCloudStorageAccountConnectionString() and should return a string containing valid credentials for an Azure blob account. 
Then, rent virtual machines on Azure and run ACESimDistributed or ACESimDistributedSaturate on each of them. Different computers will then work on different simulations.

The ReportResults folder will then contain many output files. These will then be organized along with diagrams in the C:\Primary results folder.

Overview of code

This repository contains code for applying a number of different approaches to solving extensive-form games and in particular different versions of a litigation game.

The ACESimBase folder contains the core code for finding equilibria. The GameSolvingAlgorithms folder within it contains code for a variety of different algorithms. The code that 
coordinates the use of the von Stengel et al. algorithm is located in SequenceForm.cs, which in turn executes code devoted to the algorithm (adapted from code provided von Stengel et al.)
in the ECTAAlgorithm folder. 

The code specifically relevant to the additive evidence game is contained within the AdditiveEvidneceGame folder. Some of the options used by this game are not used in this paper. 
The ManualReports folder contains code to generate the Latex code for two of the diagram types; other code to generate diagrams is in the LitigCharts project. 
The content of spreadsheets that provide cross-tabulations of all the results is defined in the AdditiveEvidenceGameReportDefinition folder.
The structure of the game tree, including the ordering of the decisions, is affected by AdditiveEvidenceGameDefinition.cs. 
The game play itself is defined in AdditiveEvidenceGame.cs, and the data structure
used to keep track of progress in the game is AdditiveEvienceGameProgress. 
The players, including chance players corresponding to different chance decisions, are listed in AdditiveEvidneceGamePlayers. 
The AdditiveEvidenceGameLauncher.cs file generates options for the many different permutations executed. 

