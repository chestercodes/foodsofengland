module OldSiteParser.Shared.ChannelProcessing

open System.Threading
open System.Threading.Channels
open System.Threading.Tasks

/// Process items in parallel using a bounded channel, consuming results sequentially.
/// processItem runs on thread pool threads with bounded concurrency.
/// consumeItem is called sequentially on a single thread — safe for EF Core / SQLite writes.
/// maxParallelism controls the maximum number of concurrent processItem invocations.
let runParallel<'TInput, 'TOutput>
    (items: 'TInput[])
    (processItem: 'TInput -> 'TOutput)
    (consumeItem: 'TOutput -> unit)
    (maxParallelism: int)
    =
    if items.Length = 0 then
        ()
    else

    let channelOpts = BoundedChannelOptions(maxParallelism * 2)
    channelOpts.SingleReader <- true
    let channel = Channel.CreateBounded<'TOutput>(channelOpts)

    let producerTask =
        task {
            try
                use semaphore = new SemaphoreSlim(maxParallelism)

                let tasks =
                    items
                    |> Array.map (fun item ->
                        task {
                            do! semaphore.WaitAsync()

                            try
                                let result = processItem item
                                do! channel.Writer.WriteAsync(result)
                            finally
                                semaphore.Release() |> ignore
                        })

                let! _ = Task.WhenAll(tasks)
                ()
            finally
                channel.Writer.TryComplete() |> ignore
        }

    let consumerTask =
        task {
            let mutable canRead = true

            while canRead do
                let! hasMore = channel.Reader.WaitToReadAsync()

                if hasMore then
                    let mutable item = Unchecked.defaultof<'TOutput>

                    while channel.Reader.TryRead(&item) do
                        consumeItem item
                else
                    canRead <- false
        }

    Task.WhenAll(producerTask :> Task, consumerTask :> Task).GetAwaiter().GetResult()
