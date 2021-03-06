﻿//--------------------------------------------------------------------------
// 
//  Copyright (c) Microsoft Corporation.  All rights reserved. 
// 
//  File: Pipeline.cs
//
//--------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Text.RegularExpressions;

namespace Baker
{
    /// <summary>Provides support for pipelined data processing.</summary>
    public static class Pipeline
    {
        internal readonly static TaskScheduler Scheduler = TaskScheduler.Default;

        /// <summary>Creates a new pipeline, with the specified function as the sole stage.</summary>
        /// <typeparam name="TInput">Specifies the type of the input data to the pipeline.</typeparam>
        /// <typeparam name="TOutput">Specifies the type of the output data from this stage of the pipeline.</typeparam>
        /// <param name="func">The function used to process input data into output data.</param>
        /// <returns>A pipeline for converting from input data to output data.</returns>
        public static Pipeline<TInput, TOutput> Create<TInput, TOutput>(Func<TInput, TOutput> func)
        {
            return Create(func, 1);
        }

     
        /// <summary>Creates a new pipeline, with the specified function as the sole stage.</summary>
        /// <typeparam name="TInput">Specifies the type of the input data to the pipeline.</typeparam>
        /// <typeparam name="TOutput">Specifies the type of the output data from this stage of the pipeline.</typeparam>
        /// <param name="func">The function used to process input data into output data.</param>
        /// <param name="degreeOfParallelism">The concurrency level for this stage of the pipeline.</param>
        /// <returns>A pipeline for converting from input data to output data.</returns>
        public static Pipeline<TInput, TOutput> Create<TInput, TOutput>(Func<TInput, TOutput> func, int degreeOfParallelism)
        {
            if (func == null) throw new ArgumentNullException("func");
            if (degreeOfParallelism < 1) throw new ArgumentOutOfRangeException("degreeOfParallelism");
            return new Pipeline<TInput, TOutput>(func, degreeOfParallelism);
        }
    }

    /// <summary>Provides support for pipelined data processing.</summary>
    /// <typeparam name="TInput">Specifies the type of the input data to the pipeline.</typeparam>
    /// <typeparam name="TOutput">Specifies the type of the output data from this stage of the pipeline.</typeparam>
    public class Pipeline<TInput, TOutput> 
    {
        protected Func<TInput, TOutput> Executor;
        private readonly int DegreeOfParallelism;

        internal Pipeline(int degreeOfParallelism) : this(null, degreeOfParallelism) { }

        internal Pipeline(Func<TInput, TOutput> func, int degreeOfParallelism)
        {
            this.Executor = func;
            this.DegreeOfParallelism = degreeOfParallelism;
        }


        /// <summary>Creates a new pipeline that combines the current pipeline with a new stage.</summary>
        /// <typeparam name="TNextOutput">Specifies the new output type of the pipeline.</typeparam>
        /// <param name="func">
        /// The function used to convert the output of the current pipeline into the new
        /// output of the new pipeline.
        /// </param>
        /// <returns>A new pipeline that combines the current pipeline with the new stage.</returns>
        /// <remarks>This overload creates a parallel pipeline stage.</remarks>
        public Pipeline<TInput, TNextOutput> Next<TNextOutput>(Func<TOutput, TNextOutput> func)
        {
            return Next(func, 1);
        }

        /// <summary>Creates a new pipeline that combines the current pipeline with a new stage.</summary>
        /// <typeparam name="TNextOutput">Specifies the new output type of the pipeline.</typeparam>
        /// <param name="processor">
        /// The processor used to convert the output of the current pipeline into the new
        /// output of the new pipeline.
        /// </param>
        /// <returns>A new pipeline that combines the current pipeline with the new stage.</returns>
        /// <remarks>This overload creates a parallel pipeline stage.</remarks>
        public Pipeline<TInput, TNextOutput> Next<TNextOutput>(IProcessor<TOutput, TNextOutput> processor)
        {
            return Next(processor.Process, 1);
        }

        /// <summary>Creates a new pipeline that combines the current pipeline with a new stage.</summary>
        /// <typeparam name="TNextOutput">Specifies the new output type of the pipeline.</typeparam>
        /// <param name="func">
        /// The function used to convert the output of the current pipeline into the new
        /// output of the new pipeline.
        /// </param>
        /// <param name="degreeOfParallelism">The concurrency level for this stage of the pipeline.</param>
        /// <returns>A new pipeline that combines the current pipeline with the new stage.</returns>
        public Pipeline<TInput, TNextOutput> Next<TNextOutput>(Func<TOutput, TNextOutput> func, int degreeOfParallelism)
        {
            if (func == null) throw new ArgumentNullException("func");
            if (degreeOfParallelism < 1) throw new ArgumentOutOfRangeException("degreeOfParallelism");
            return new InternalPipeline<TNextOutput>(this, func, degreeOfParallelism);
        }

        /// <summary>Runs the pipeline and returns an enumerable over the results.</summary>
        /// <param name="source">The source data to be processed by the pipeline.</param>
        /// <returns>An enumerable of the results of the pipeline.</returns>
        public IEnumerable<TOutput> On(IEnumerable<TInput> source)
        {
            return On(source, new CancellationToken());
        }

        /// <summary>Runs the pipeline and returns an enumerable over the results.</summary>
        /// <param name="source">The source data to be processed by the pipeline.</param>
        /// <param name="cancellationToken">The cancellation token used to signal cancellation of the pipelining.</param>
        /// <returns>An enumerable of the results of the pipeline.</returns>
        public IEnumerable<TOutput> On(IEnumerable<TInput> source, CancellationToken cancellationToken)
        {
            // Validate arguments
            if (source == null) throw new ArgumentNullException("source");
            return ProcessNoArgValidation(source, cancellationToken);
        }        


        /// <summary>Runs the pipeline and returns an enumerable over the results.</summary>
        /// <param name="source">The source data to be processed by the pipeline.</param>
        /// <param name="cancellationToken">The cancellation token used to signal cancellation of the pipelining.</param>
        /// <returns>An enumerable of the results of the pipeline.</returns>
        private IEnumerable<TOutput> ProcessNoArgValidation(IEnumerable<TInput> source, CancellationToken cancellationToken)
        {
            // Create a blocking collection for communication with the query running in a background task
            using (var output = new BlockingCollection<TOutput>())
            {
                // Start a task to run the core of the stage
                var processingTask = Task.Factory.StartNew(() =>
                {
                    try { ProcessCore(source, cancellationToken, output); }
                    finally { output.CompleteAdding(); }
                }, CancellationToken.None, TaskCreationOptions.None, Pipeline.Scheduler);

                // Enumerate and yield the results.  This makes ProcessNoArgValidation
                // lazy, in that processing won't start until enumeration begins.
                foreach (var result in output.GetConsumingEnumerable(cancellationToken))
                {
                    yield return result;
                }

                // Make sure the processing task has shut down, and propagate any exceptions that occurred
                processingTask.Wait();
            }
        }

        /// <summary>Implements the core processing for a pipeline stage.</summary>
        /// <param name="source">The source data to be processed by the pipeline.</param>
        /// <param name="cancellationToken">The cancellation token used to signal cancellation of the pipelining.</param>
        /// <param name="output">The collection into which to put the output.</param>
        protected virtual void ProcessCore(IEnumerable<TInput> source, CancellationToken cancellationToken, BlockingCollection<TOutput> output)
        {
            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = DegreeOfParallelism,
                TaskScheduler = Pipeline.Scheduler
            };
            Parallel.ForEach(source, options, item => output.Add(Executor(item)));
        }

        /// <summary>Helper used to add a new stage to a pipeline.</summary>
        /// <typeparam name="TNextOutput">Specifies the type of the output for the new pipeline.</typeparam>
        private sealed class InternalPipeline<TNextOutput> : Pipeline<TInput, TNextOutput>
        {
            private readonly Pipeline<TInput, TOutput> _beginningPipeline;
            private readonly Func<TOutput, TNextOutput> _lastStageFunc;

            public InternalPipeline(Pipeline<TInput, TOutput> beginningPipeline, Func<TOutput, TNextOutput> func, int degreeOfParallelism)
                : base(degreeOfParallelism)
            {
                _beginningPipeline = beginningPipeline;
                _lastStageFunc = func;
            }

            /// <summary>Implements the core processing for a pipeline stage.</summary>
            /// <param name="source">The source data to be processed by the pipeline.</param>
            /// <param name="cancellationToken">The cancellation token used to signal cancellation of the pipelining.</param>
            /// <param name="output">The collection into which to put the output.</param>
            protected override void ProcessCore(
                IEnumerable<TInput> source, CancellationToken cancellationToken, BlockingCollection<TNextOutput> output)
            {
                var options = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = DegreeOfParallelism,
                    TaskScheduler = Pipeline.Scheduler
                };
                Parallel.ForEach(_beginningPipeline.On(source, cancellationToken), options, item => {
                    if (item == null)
                        return;

                    var o = _lastStageFunc(item);
                    if(o != null)
                        output.Add(o);
                });
            }
        }
    }
}