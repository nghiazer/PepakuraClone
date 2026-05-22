using FourHUnfolder.Domain.Models;

namespace FourHUnfolder.Application.Interfaces;

public interface IMeshLoader
{
    Mesh Load(string filePath);
}
