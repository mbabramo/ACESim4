# ACESim ArrayProcessing Module – Introduction and Overview

The ArrayProcessing module provides a framework for constructing and executing sequences of operations on arrays of doubles, expressed as commands in a custom intermediate representation. It is designed to describe algorithms in a compact form and then transform them into efficient executable code. 

At the core, the module allows you to build an **ArrayCommandList**, which acts like a mini assembly language for array manipulations. Each operation is represented as an **ArrayCommand**, labeled by an **ArrayCommandType** (e.g., arithmetic, comparisons, data movement, or flow control). These commands operate on a *virtual stack* of array slots, which includes both original input/output values and scratch space for intermediate results.

Key features include:

- **Command Abstraction:** Operations like zeroing, copying, incrementing, multiplying, and conditional comparisons are represented as commands. This allows algorithms to be described declaratively, before any execution occurs.
- **Index Management:** The system tracks indices for both original array entries and scratch (temporary) values. Special handling is provided for ordered sources (inputs) and ordered destinations (outputs), which optimize memory access by batching reads and writes.
- **Conditional Execution:** Basic branching is supported via comparison commands combined with `If` and `EndIf` markers, enabling conditional execution of blocks of commands.
- **Chunking and Execution Model:** Commands are grouped into **chunks**, which form nodes of a tree representing the computation structure. Chunks may be executed sequentially or in parallel depending on configuration, and conditionals translate into branches within this tree.
- **Parallelization:** When enabled, chunks marked as parallelizable can be executed concurrently using internal mechanisms, improving performance on multi-core systems.
- **Code Generation:** Once a command list is complete, it can be compiled into executable code. The system may emit IL directly or use Roslyn to generate C# source, compile it, and run it. Sequences that exceed a configurable threshold are automatically JIT-compiled for efficiency.
- **Optimizations:** 
  - Reuse of identical command ranges avoids duplicating logic.
  - Scratch indices can be reused when safe to minimize memory usage.
  - Ordered sources and destinations reduce locking overhead by deferring reads and writes through contiguous buffers.
  - Large if-blocks can be hoisted and split to avoid oversized methods.

The ArrayProcessing module thus acts as a compiler pipeline: high-level algorithms are expressed via **ArrayCommandList**, analyzed and transformed into a tree of **ArrayCommandChunk** objects, optionally optimized (e.g., by hoisting), and finally compiled into fast delegates that operate directly on arrays of doubles.

## ArrayCommandType (Enum)

The **`ArrayCommandType`** enumeration defines the types of operations that can be recorded in an array-processing command sequence. It includes basic data moves, arithmetic operations, comparisons, and flow control markers. For example: 

- **Data operations:** `Zero` (set a value to 0), `CopyTo` (copy a value to a target index), `NextSource`/`NextDestination` (read next input or write next output in ordered lists), and `ReusedDestination` (mark a repeated output target for accumulation). 
- **Arithmetic:** `MultiplyBy`, `IncrementBy`, `DecrementBy` for in-place multiplication/addition/subtraction. 
- **Comparisons:** `EqualsOtherArrayIndex`, `NotEqualsOtherArrayIndex`, `GreaterThanOtherArrayIndex`, `LessThanOtherArrayIndex` (compare values at two indices), and `EqualsValue`, `NotEqualsValue` (compare an index’s value to a constant). 
- **Flow control:** `If` and `EndIf` serve as markers delimiting a conditional block of commands, and `Blank` represents a no-op placeholder. 

These enum values are used throughout the module to instruct how each `ArrayCommand` should behave when executed or compiled.

## ArrayCommand (Struct)

The **`ArrayCommand`** struct represents a single operation in the sequence. It is an immutable, serializable struct containing: 

- **CommandType:** an `ArrayCommandType` indicating what operation to perform. 
- **Index:** the target index the command operates on (e.g. where a value is stored or which value to modify). 
- **SourceIndex:** an optional second index (e.g. the source of a copy or the operand for arithmetic).

**Purpose:** `ArrayCommand` acts as a low-level instruction in a mini “array assembly language.” For example, a command might mean *“copy value from index X to a new index Y”* or *“increment value at index I by the value at J.”* It abstracts operations on an underlying array of doubles (the *virtual stack*).

**Major methods and properties:**

- **Constructor:** Ensures indices are valid. It throws an exception if either index is below –1 (which signifies “no index”) except for a special `CheckpointTrigger` value. This guards against invalid usage of indices.  
- **ToString():** Returns a readable string like `"CommandType Index source:SourceIndex"` for debugging.  
- **Clone():** Creates a duplicate command.  
- **GetSourceIndexIfUsed():** Returns the `SourceIndex` if the command type uses one, or –1 otherwise.  
- **GetTargetIndexIfUsed():** Returns the `Index` if it is a meaningful target, or –1 otherwise.  
- **WithIndex(...) / WithSourceIndex(...):** Create modified copies with new indices.  
- **Equality (`Equals`, `GetHashCode`, `==`, `!=`):** Two commands are equal if type, index, and source all match.  

Overall, `ArrayCommand` is the smallest unit of work in the array-processing logic. Higher-level classes use it to build algorithms that can later be compiled into executable code.

## ArrayCommandList (Core fields and state)

The **`ArrayCommandList`** class is the core of the ArrayProcessing module. It is responsible for **building a sequence of ArrayCommands**, organizing them for execution, and initiating code generation. It acts as a builder for algorithms that manipulate a virtual stack of doubles. Its responsibilities include:

- Managing an array of commands,  
- Providing high-level methods to append operations while allocating indices for results,  
- Handling ordered input and output buffers,  
- Supporting conditional execution,  
- Organizing commands into chunks for structured execution,  
- Optionally enabling parallel execution, and  
- Triggering compilation into executable code.  

**Fields and state:**

- `UnderlyingCommands`: array holding the sequence of `ArrayCommand` instructions.  
- `NextCommandIndex`: the next open slot in `UnderlyingCommands`.  
- `NextArrayIndex`: the next available index in the virtual array (for intermediate values).  
- `MaxArrayIndex`: the highest index used so far.  
- **Parallelization flag:** passed to the constructor, enabling multi-threaded execution of chunks.  
- **Ordered I/O structures:** `OrderedSourceIndices` and `OrderedDestinationIndices` track contiguous lists of original indices for batched reading and writing. `ReusableOrderedDestinationIndices` maps repeated outputs to slots for accumulation.  
- `Checkpoints`: list for checkpoint values when enabled.  
- `CheckpointTrigger = -2`: a marker index for checkpoint commands.  
- Settings flags such as  `UseCheckpoints`, `UseRoslyn`. These control optimizations, memory reuse, and code generation strategy.  

**Command addition mechanism:**  

All high-level operations use a private `AddCommand(ArrayCommand cmd)` method to append instructions. Its behavior:  

- Inserts an initial `Blank` command at the start if needed.  
- If repeating an identical command range, verifies commands match without duplicating them.  
- Otherwise, appends the command and increments `NextCommandIndex`.  

**Automatic chunk creation:**  

When finalizing the list, contiguous commands not explicitly wrapped in chunks are automatically placed into chunks. This ensures all commands belong to the execution tree.  

**Reusing identical command sequences:**  

`StartCommandChunk` can be called with a reference to a previously recorded range. This allows reuse of the same commands across multiple cases without duplication, reducing memory and compilation overhead.  

**Nested depth management:**  

`IncrementDepth()` and `DecrementDepth()` are available to manage nested contexts, ensuring scratch indices and parent-child relationships remain consistent across chunks.  

Together, these mechanisms provide the low-level infrastructure for recording, structuring, and preparing array operations before higher-level optimizations and code generation are applied.

## ArrayCommandList (Operations)

`ArrayCommandList` provides high-level methods for building algorithms out of low-level commands. These methods hide details of `ArrayCommandType` and manage allocation of indices, so the user does not manipulate scratch space directly.

**Creating new values (scratch space):**

- `NewZero()` – allocate a new index initialized to 0.  
- `NewUninitialized()` – allocate a new index without setting a value.  
- `CopyToNew(sourceIndex, fromOriginalSources)` – copy a value to a new index. If ordered sources are enabled and `fromOriginalSources` is true, the source index is queued and a `NextSource` command defers loading until execution. Otherwise, a direct `CopyTo` is emitted. Overloads accept arrays of indices.  

**Binary operations that produce new values:**

- `AddToNew(index1, fromOriginal, index2)` – result is `index1 + index2` in a new slot.  
- `MultiplyToNew(index1, fromOriginal, index2)` – result is `index1 * index2` in a new slot.  

**In-place modifications of existing values:**

- `ZeroExisting(index)` – reset a slot to 0.  
- `CopyToExisting(destIndex, sourceIndex)` – copy one slot into another.  
- `MultiplyBy(index, multiplierIndex)` – multiply slot by another. Restricted for original indices.  
- `Increment(index, targetOriginal, incrementIndex)` – add a value into a slot. If targeting original indices, this uses ordered destinations:
  - First time: adds to `OrderedDestinationIndices` and emits `NextDestination`.  
  - Repeated increments: emit `ReusedDestination` to accumulate into the same slot.  
- `IncrementArrayBy(indices, targetOriginal, incIndex)` and variants – apply increment across multiple targets.  
- `Decrement(index, decrementIndex)` – subtract one slot from another, with similar restrictions to `MultiplyBy`.  
- `DecrementArrayBy(indices, decIndex)` – apply decrements across multiple targets.  
- `IncrementByProduct(targetIndex, original, index1, index2)` – multiply two values and add result into target. Frees temporary slot if scratch reuse is enabled.  
- `DecrementByProduct(...)` – subtract product from target.  

**Vectorized utilities:**

- `MultiplyArrayBy(indices, multiplierIndex)` – multiply multiple slots by a common factor.  
- `MultiplyArrayBy(indices, multipliers)` – elementwise multiply.  
- `DecrementArrayBy(indices, decrementIndex)` – subtract common value from many slots.  

**Miscellaneous:**

- `InsertBlankCommand()` – insert a no-op placeholder.  
- `CreateCheckpoint(sourceIndex)` – record checkpoint if enabled by writing to `CheckpointTrigger`.  

These operations form the vocabulary for describing array computations declaratively. They automatically manage allocation of scratch indices, ensure reuse policies are followed, and interact correctly with ordered sources and destinations.

## ArrayCommandList (Conditionals)

`ArrayCommandList` supports conditional execution using comparison commands combined with `If` and `EndIf` markers. This allows sections of commands to be executed only when a condition is true.

**Comparison commands:**

- `InsertEqualsOtherArrayIndexCommand(index1, index2)` – compare two slots for equality.  
- `InsertNotEqualsOtherArrayIndexCommand(index1, index2)` – compare for inequality.  
- `InsertGreaterThanOtherArrayIndexCommand(index1, index2)` – compare if one slot is greater.  
- `InsertLessThanOtherArrayIndexCommand(index1, index2)` – compare if one slot is less.  
- `InsertEqualsValueCommand(index, constant)` – compare a slot against a constant.  
- `InsertNotEqualsValueCommand(index, constant)` – inequality check against a constant.  

These commands evaluate a condition at runtime and set an internal flag without changing array contents.

**Flow control markers:**

- `InsertIfCommand()` – inserts an `If` marker. This also calls `KeepCommandsTogether()` to prevent chunking from splitting inside the block.  
- `InsertEndIfCommand()` – inserts an `EndIf` marker and calls `EndKeepCommandsTogether()`.  

Between `If` and `EndIf`, subsequent commands execute only if the last comparison succeeded. There is no direct `Else` construct; separate `If` blocks must be used to emulate that behavior.

**Structural helpers:**

- `KeepCommandsTogether()` / `EndKeepCommandsTogether()` – increment and decrement a counter that ensures the commands between remain in the same chunk.  
- Conditionals can be nested. Matching `If` and `EndIf` pairs are required for correct execution.  

This mechanism enables simple branching in the array-processing model, so that chunks of commands may be conditionally skipped at runtime.

## ArrayCommandList (Chunking and Verification)

Once a sequence of commands is built, `ArrayCommandList` organizes them into a hierarchy of **chunks** for execution. Chunks group contiguous commands and become nodes of a tree structure, representing both linear sequences and conditional branches.

**Chunk organization:**

- The command list is divided into ranges, each forming an `ArrayCommandChunk`.  
- Conditionals (`If`/`EndIf`) cause the creation of sub-chunks representing the true-block of the branch.  
- If explicit chunk boundaries are not created, the system automatically inserts them during finalization to ensure every command belongs to a chunk.  
- Identical command ranges can be reused across multiple chunks, reducing duplication and memory use.  

**Verification and relationships:**

- After chunking, each chunk is analyzed for its usage of the virtual stack.  
- Methods populate fields such as `FirstReadFromStack`, `LastUsed`, and `TranslationToLocalIndex`, which record how each index is read or written.  
- Parent-child relationships are established so that values computed in a child chunk are available to the parent when needed.  
- `CopyIncrementsToParent` records indices that must be propagated upward after a child finishes.  

**Merging and repeated chunks:**

- When ending a chunk, optional parameters specify which values should be merged into the parent and whether the chunk is part of a repeated identical sequence.  
- This ensures correct accumulation of results and avoids redundant storage of repeated blocks.  

The chunking process transforms a linear list of commands into a tree of structured blocks, each with explicit data dependencies and control flow, preparing the sequence for efficient execution or compilation.

## ArrayCommandList (Code Generation)

After building and chunking, `ArrayCommandList` can compile commands into executable code for performance.

**Execution options:**

- **Interpretation:** Commands may be executed directly in sequence, but this is less efficient.  
- **IL emission:** `ILChunkEmitter` translates chunks into dynamic methods (delegates) using MSIL instructions.  
- **Roslyn compilation:** Chunks can be converted into C# source, compiled, and invoked at runtime.  

**Automatic compilation threshold:**

- A configurable limit (`MinNumCommandsToCompile`, default 25) determines when a sequence is compiled instead of interpreted.  
- Sequences below the threshold run interpretively; longer ones are JIT-compiled.  

**Checkpoints:**

- If enabled, checkpoint commands copy values to the `CheckpointTrigger` index.  
- During Roslyn compilation, checkpoints inject code to record values for debugging or verification.  

**Compilation flow:**

1. `CompleteCommandList()` finalizes chunking and inserts any missing structural nodes.  
2. Optional hoisting and splitting are applied to avoid oversized methods.  
3. Chunks are compiled using the chosen backend. Delegates are stored in a dictionary keyed by chunk identifiers.  
4. Execution starts with the root chunk, passing in the virtual stack, ordered sources, ordered destinations, and pointers for input/output arrays.  

**Parallelization in code generation:**

- If parallelization is enabled, chunks marked as parallelizable are emitted with code that can be executed concurrently using internal scheduling.  

This compilation stage transforms a declarative list of commands into optimized, directly executable code, reducing the overhead of interpretation for large computations.

## ArrayCommandChunk (Class)

**`ArrayCommandChunk`** represents a group of commands (a contiguous range from the `UnderlyingCommands`) that will be executed as a unit. It is defined as a nested class inside `ArrayCommandList`. Its role is to hold metadata about a chunk of commands, particularly for code generation and linking chunks together.

**Key fields:**

- **ID:** Unique identifier assigned incrementally.  
- **StartCommandRange / EndCommandRangeExclusive:** Range of commands in `UnderlyingCommands` belonging to this chunk.  
- **LastChild:** Highest branch ID among this chunk’s children.  
- **ChildrenParallelizable:** Whether child chunks can be executed in parallel.  

**Virtual stack usage:**

- `FirstReadFromStack`, `FirstSetInStack`, `LastSetInStack`, `LastUsed`: arrays noting when each slot is accessed.  
- `TranslationToLocalIndex`: mapping of global stack indices to this chunk’s local space.  
- `IndicesReadFromStack` / `IndicesInitiallySetInStack`: lists of indices relevant to this chunk.  
- `VirtualStack`: optional working storage (primarily used in interpretation or debugging).  

**Parent and child relations:**

- `ParentVirtualStack` and `ParentVirtualStackID`: references to parent stack if sharing occurs.  
- `CopyIncrementsToParent`: indices whose updates must propagate upward.  
- `Skip`: marks the chunk as skipped (e.g., false branch of an if).  

**Other fields:**

- `SourcesInBody` / `DestinationsInBody`: counts of ordered sources and destinations consumed here.  
- `ExecId`: identifier for profiling or tracking.  
- `LargeBodies`: auxiliary info about if-bodies, used in optimization.  
- `Name` and `CompiledCode`: debugging metadata and generated code representation.  

**Methods:**

- Constructor: assigns ID.  
- `ToString()`: debug output showing ranges, increments to parent, and stack usage summaries.  

**Role in execution:**  

An `ArrayCommandChunk` captures the static structure of a block of commands—its bounds, data usage, and dependencies. `ArrayCommandList` creates these chunks, fills their metadata, and arranges them in a tree. Code generators then rely on these chunks to emit correct executable code and manage data flow across the algorithm.

## ArrayCommandChunkDelegate (Delegate Type)

`ArrayCommandChunkDelegate` is a delegate type defining the signature of compiled chunk methods. Its definition is:

public delegate void ArrayCommandChunkDelegate(
  double[] vs,
  double[] os,
  double[] od,
  ref int cosi,
  ref int codi
);

Any compiled chunk method will conform to this signature:

- **vs**: the virtual stack array of doubles, holding intermediate and scratch values.  
- **os**: ordered source values, copied from original inputs at specified indices.  
- **od**: ordered destination outputs, to be written back to original indices after execution.  
- **ref int cosi**: current ordered source index, advanced as values are consumed.  
- **ref int codi**: current ordered destination index, advanced as outputs are produced.  

By passing `cosi` and `codi` by reference, changes made inside a chunk persist for subsequent chunks, ensuring correct sequencing of input consumption and output production.

This delegate type provides the interface through which dynamically compiled methods are invoked. It links the command execution model (with arrays and ordered indices) to the actual emitted code, ensuring all compiled chunks have a consistent, strongly typed entry point.

## ILChunkEmitter (Class)

**`ILChunkEmitter`** generates runtime code for a chunk of commands by emitting IL instructions into a dynamic method. It translates abstract `ArrayCommand` operations into executable form and returns an `ArrayCommandChunkDelegate` for invocation.

**Purpose:**  
Produce efficient machine code for a given `ArrayCommandChunk` by compiling its commands directly into IL.

**Construction:**  
Takes an `ArrayCommandChunk` and the full command array. Tracks the command range to emit.

**EmitMethod:**

- Defines a `DynamicMethod` with the signature matching `ArrayCommandChunkDelegate`.  
- Declares locals: working copies of `cosi` and `codi` (for ordered sources/destinations), plus a condition flag.  
- Initializes locals from the by-ref arguments.  
- Iterates over commands in the chunk and calls type-specific emitters.  
- At the end, writes updated `cosi` and `codi` back to the by-ref arguments.  
- Returns a delegate bound to the dynamic method.

**Command translation examples:**

- **Zero:** `vs[target] = 0.0`.  
- **CopyTo:** `vs[target] = vs[source]`.  
- **NextSource:** `vs[target] = os[localCosi++]`.  
- **NextDestination:** `od[localCodi++] = vs[source]`.  
- **ReusedDestination:** `od[reusedIndex] += vs[source]`.  
- **IncrementBy / DecrementBy / MultiplyBy:** apply arithmetic directly on array slots.  

**Conditionals:**  
Comparisons set a local condition flag. `If` emits a branch that skips to a label if the flag is false. `EndIf` marks the label, completing the conditional structure. A stack of labels (`IfBlockInfo`) ensures nested if-blocks are handled correctly.

**Ordered sources/destinations:**  
IL uses local counters to index into `os` and `od`. Updates to counters persist by writing them back to `cosi` and `codi`.

**Performance:**  
Dynamic methods run efficiently, avoiding interpreter overhead. Each operation translates into a few array accesses and arithmetic opcodes. Chunk size is bounded to avoid exceeding IL limits; very large chunks may be split or compiled using Roslyn instead.

In summary, `ILChunkEmitter` is the backend translating the abstract command representation into optimized, executable IL, producing delegates that can run array-processing logic at near-native speed.

## HoistPlanner (Class)

The **`HoistPlanner`** scans the command tree to locate oversized sections of code that need splitting. It identifies if-blocks that exceed a size threshold and records them for later restructuring.

**Purpose:**  
Prevent chunks from becoming too large to compile efficiently by flagging large conditional bodies for hoisting.

**PlanEntry record:**  

- `LeafId`: ID of the chunk that is oversized.  
- `IfIdx`: index of the starting `If` command.  
- `EndIfIdx`: index of the matching `EndIf`.  
- `BodyLen`: number of commands inside the if-body.  

**Operation:**  

- Constructed with the full command array and a maximum commands per chunk.  
- `BuildPlan(root)` traverses the chunk tree:  
  - If a leaf’s size exceeds the maximum, it looks for an outermost `If`/`EndIf`.  
  - If found, creates a plan entry describing that body.  
  - Leaves with no conditionals are ignored even if too large.  

**Scope:**  
HoistPlanner makes no modifications itself. It only produces a list of oversized if-blocks, which will later be consumed by the HoistMutator.

This analysis ensures large conditional blocks can be safely restructured before code generation.

## HoistMutator (Class)

The **`HoistMutator`** applies the plan produced by the HoistPlanner and restructures the command tree to break up oversized chunks. It inserts new structural nodes and slices large if-bodies into smaller children while preserving algorithm semantics.

**Purpose:**  
Transform the command tree so no leaf chunk exceeds the configured size limit, ensuring all sections can be compiled and executed reliably.

**Operation:**

- **ApplyPlan(acl, plan):**
  - For each plan entry:
    - Locate the target leaf by ID.  
    - Replace the leaf with a new conditional gate node.  
    - Split the original if-body into multiple child chunks, each within size limits.  
    - Preserve any prefix or postfix commands outside the if-body as separate chunks.  
    - Rebuild relationships so parent and child chunks exchange data correctly.  

- **Tree maintenance:**
  - Inserts new nodes into the existing `CommandTree`, replacing the original oversized leaf.  
  - Calls analysis routines (`SetupVirtualStack`, `SetupVirtualStackRelationships`) to recompute usage and dependencies after restructuring.  
  - Updates chunk metadata such as ranges, children, and data flow propagation.  

**Data integrity:**  
During splitting, indices used across boundaries are marked to be copied up to the parent, ensuring results remain available across slices. This maintains correctness even as the body is divided.

**Effect:**  
The resulting command tree consists of balanced chunks, none exceeding the maximum size. The structure remains faithful to the intended algorithm, but is partitioned into manageable units for code generation.

## Interactions Between the Classes

The ArrayProcessing module functions as a pipeline, with each class contributing to turning high-level descriptions of computations into executable code.

**ArrayCommandList as orchestrator:**  
This class is central. It creates and stores `ArrayCommand` instances, manages ordered sources and destinations, and builds a tree of `ArrayCommandChunk` objects. It provides the high-level API for describing algorithms and coordinates all subsequent steps.

**ArrayCommand and ArrayCommandType usage:**  
Every operation is represented as an `ArrayCommand` tagged with an `ArrayCommandType`. The type guides later stages: chunk building, IL emission, or Roslyn code generation.

**ArrayCommandChunk in the command tree:**  
Commands are grouped into `ArrayCommandChunk` nodes. These chunks structure the computation into manageable blocks and carry metadata about data dependencies, control flow, and stack usage. The chunk tree directly reflects the logical structure of the algorithm.

**Delegates for execution:**  
Each chunk can be compiled into a method matching the `ArrayCommandChunkDelegate` signature. These delegates provide the interface between compiled code and the runtime data arrays (`vs`, `os`, `od`), while managing ordered index counters.

**ILChunkEmitter as backend:**  
This class translates the abstract representation into efficient IL, ensuring every command type is realized as concrete array and arithmetic instructions, with conditionals compiled into branches.

**HoistPlanner and HoistMutator:**  
Together, these ensure oversized chunks are split. HoistPlanner analyzes the tree to identify large if-bodies, while HoistMutator restructures the tree by inserting gate nodes and slicing bodies into smaller chunks. This keeps code within size limits and maintains correctness of data flow.

**Execution flow:**  
1. A user builds operations through `ArrayCommandList`.  
2. The list is chunked into a tree of `ArrayCommandChunk` objects.  
3. Optional hoisting refactors large bodies.  
4. Chunks are compiled into delegates by ILChunkEmitter or Roslyn.  
5. Execution runs the root chunk’s delegate, which consumes inputs, processes intermediates, and produces outputs.  

In this way, the classes form a layered system: commands define the computation, chunks structure it, planners and mutators optimize it, and emitters generate efficient code that executes with ordered data movement and parallelizable structure.
